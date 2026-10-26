param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-contracts-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null
$fileContract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$cases = @(
    @{ Name = 'file_contract'; Code = $fileContract + 'func main(): none {}'; Contract = $true },
    @{ Name = 'forward_destructor'; Code = 'extern native("stdio.h") { resource OwnedFile: FILE* drop fclose; func fclose(value: FILE*): c_int; type FILE; } func main(): none {}'; Contract = $true },
    @{ Name = 'module_contract'; Code = 'module handles { extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } } func main(): none {}'; Contract = $true },
    @{ Name = 'void_pointer_contract'; Code = 'extern native("stdlib.h") { func free(value: c_void*): none; resource OwnedMemory: c_void* drop free; } func main(): none {}' },
    @{ Name = 'contextual_words'; Code = 'func main(): none { var resource: i32 = 1; var drop: i32 = 2; }' },
    @{ Name = 'missing_destructor'; Code = 'extern native("stdio.h") { type FILE; resource OwnedFile: FILE* drop absent; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'scalar_representation'; Code = 'extern native("stdlib.h") { func abs(value: c_int): c_int; resource OwnedNumber: c_int drop abs; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'unknown_pointee'; Code = 'extern native("stdlib.h") { func free(value: Unknown*): none; resource OwnedMemory: Unknown* drop free; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'pointer_to_pointer'; Code = 'extern native("stdio.h") { type FILE; func close(value: FILE**): none; resource OwnedFile: FILE** drop close; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'wrong_destructor_arity'; Code = 'extern native("stdio.h") { type FILE; func fclose(): c_int; resource OwnedFile: FILE* drop fclose; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'wrong_destructor_parameter'; Code = 'extern native("stdio.h") { type FILE; func close(value: c_void*): none; resource OwnedFile: FILE* drop close; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'wrong_destructor_return'; Code = 'extern native("stdio.h") { type FILE; func close(value: FILE*): FILE*; resource OwnedFile: FILE* drop close; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'wrong_native_origin'; Code = 'extern native("stdio.h") { type FILE; resource OwnedFile: FILE* drop close; } extern native("stdlib.h") { func close(value: FILE*): none; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'wrong_link_library'; Code = 'extern native("stdio.h", "not_linked") { type FILE; resource OwnedFile: FILE* drop fclose; } extern native("stdio.h") { func fclose(value: FILE*): c_int; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'sage_destructor'; Code = 'extern native("stdio.h") { type FILE; resource OwnedFile: FILE* drop close; } module native { unsafe func close(value: FILE*): none {} } func main(): none {}'; Error = 'S190' },
    @{ Name = 'duplicate_resource'; Code = $fileContract + 'extern native("stdio.h") { resource OwnedFile: FILE* drop fclose; } func main(): none {}'; Error = 'S150' },
    @{ Name = 'struct_name_collision'; Code = $fileContract + 'struct OwnedFile { value: i32; } func main(): none {}'; Error = 'S150' },
    @{ Name = 'native_alias_name_collision'; Code = 'extern native("stdlib.h") { func free(value: c_void*): none; resource c_int: c_void* drop free; } func main(): none {}'; Error = 'S190' },
    @{ Name = 'nullable_local_value'; Code = $fileContract + 'unsafe func main(): none { var value: OwnedFile = (0 as FILE*) as OwnedFile; }' },
    @{ Name = 'unsupported_resource_cast'; Code = $fileContract + 'unsafe func main(): none { (0 as FILE*) as OwnedFile; }'; Error = 'S192' },
    @{ Name = 'resource_type_is_not_value'; Code = $fileContract + 'func main(): none { OwnedFile; }'; Error = 'S191' },
    @{ Name = 'unsupported_facade_pointer'; Code = $fileContract + 'unsafe func main(): none { var value: OwnedFile* = 0 as OwnedFile*; }'; Error = 'S191' },
    @{ Name = 'invalid_return_value'; Code = $fileContract + 'func make(): OwnedFile { return 0; } func main(): none {}'; Error = 'S193' },
    @{ Name = 'borrowed_parameter'; Code = $fileContract + 'func use_value(value: &OwnedFile): none {} func main(): none {}' },
    @{ Name = 'unsupported_array'; Code = $fileContract + 'func main(): none { var values: OwnedFile[2]; }'; Error = 'S191' },
    @{ Name = 'declared_resource_field'; Code = $fileContract + 'struct Box { value: OwnedFile; } func main(): none {}' },
    @{ Name = 'unsupported_native_signature'; Code = $fileContract + 'extern other("stdio.h") { func invalid(value: OwnedFile): none; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'imported_contract'; Code = 'use resource_module; func main(): none {}' },
    @{ Name = 'transitive_destructor'; Code = 'use resource_spec; func main(): none {}' },
    @{ Name = 'imported_opaque_collision'; Code = 'use resource_module; use conflicting_opaque; func main(): none {}'; Error = 'S150' },
    @{ Name = 'imported_error_origin'; Code = 'use invalid_resource; use later_module; func main(): none {}'; Error = 'S190'; File = 'invalid_resource.sg' }
)

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'resource_module.sg') -Value $fileContract
    Set-Content -LiteralPath (Join-Path $sourceDir 'invalid_resource.sg') -Value 'extern native("stdio.h") { type FILE; resource BadFile: FILE* drop absent; }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'later_module.sg') -Value 'module later_module { func value(): i32 { return 1; } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'resource_spec.sg') -Value 'use resource_drop; extern native("stdio.h") { type FILE; resource OwnedFile: FILE* drop fclose; }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'resource_drop.sg') -Value 'extern native("stdio.h") { func fclose(value: FILE*): c_int; }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'conflicting_opaque.sg') -Value 'extern other("stdio.h") { type OwnedFile; }'
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath --verbose $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
            if ($case.File -and $text -notmatch ([regex]::Escape($case.File) + '\(\d+,\d+\): error ' + $case.Error)) {
                throw "$($case.Name): expected originating file $($case.File).`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected validated declaration and successful compilation.`n$text"
        } elseif ($case.Contract) {
            $ast = Get-Content -LiteralPath (Join-Path $probeDir "obj/$($case.Name).ast") -Raw
            $header = Get-Content -LiteralPath (Join-Path $probeDir "obj/$($case.Name).h") -Raw
            if ($ast -notmatch 'native::fclose' -or $header -notmatch 'FILE\* _sage_handle') {
                throw "$($case.Name): expected resolved destructor origin in AST and a typed native facade."
            }
        }
    }
    Write-Output "Native resource contract checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
