param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-returns-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$factory = 'func make(): OwnedFile { unsafe { const owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$cases = @(
    @{ Name = 'safe_factory'; Code = $factory + 'func main(): none { var owner: OwnedFile = make(); }' },
    @{ Name = 'const_result'; Code = $factory + 'func main(): none { const owner: OwnedFile = make(); var moved: OwnedFile = owner; }' },
    @{ Name = 'bound_forwarding'; Code = $factory + 'func forward(): OwnedFile { var owner: OwnedFile = make(); return owner; } func main(): none { var owner: OwnedFile = forward(); }' },
    @{ Name = 'branch_returns'; Code = 'func choose(flag: b8): OwnedFile { unsafe { var a: OwnedFile = (0 as FILE*) as OwnedFile; if (flag) { return a; } var b: OwnedFile = a; return b; } } func main(): none { var owner: OwnedFile = choose(false); }' },
    @{ Name = 'module_factory'; Code = 'module files { ' + $factory + '} func main(): none { var owner: OwnedFile = files::make(); }' },
    @{ Name = 'forward_resource_type'; Code = $factory + $contract + 'func main(): none { var owner: OwnedFile = make(); }'; NoPrefix = $true },
    @{ Name = 'imported_factory'; Code = 'use resource_factory; func main(): none { var owner: OwnedFile = make(); }'; NoPrefix = $true },
    @{ Name = 'discarded_result'; Code = $factory + 'func main(): none { make(); }'; Error = 'S193' },
    @{ Name = 'anonymous_raw_projection'; Code = $factory + 'unsafe func main(): none { var raw: FILE* = make() as FILE*; }'; Error = 'S193' },
    @{ Name = 'anonymous_member_projection'; Code = $factory + 'func main(): none { make()._sage_handle; }'; Error = 'S193' },
    @{ Name = 'anonymous_comparison'; Code = $factory + 'func main(): none { var same: b8 = make() == make(); }'; Error = 'S193' },
    @{ Name = 'direct_factory_return'; Code = $factory + 'func forward(): OwnedFile { return make(); } func main(): none {}'; Error = 'S193' },
    @{ Name = 'direct_adoption_return'; Code = 'unsafe func make(): OwnedFile { return (0 as FILE*) as OwnedFile; } func main(): none {}'; Error = 'S193' },
    @{ Name = 'nonowner_return'; Code = 'func make(): OwnedFile { return 0; } func main(): none {}'; Error = 'S193' },
    @{ Name = 'borrowed_return'; Code = 'func steal(value: &OwnedFile): OwnedFile { return value; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'move_after_return_path'; Code = 'unsafe func make(flag: b8): OwnedFile { var owner: OwnedFile = (0 as FILE*) as OwnedFile; var moved: OwnedFile = owner; return owner; } func main(): none {}'; Error = 'S140' },
    @{ Name = 'return_with_live_loan'; Code = 'unsafe func make(): OwnedFile { var owner: OwnedFile = (0 as FILE*) as OwnedFile; var view: &OwnedFile = &owner; return owner; } func main(): none {}'; Error = 'S178' },
    @{ Name = 'missing_return'; Code = 'func make(): OwnedFile {} func main(): none {}'; Error = 'S163' },
    @{ Name = 'entry_point_resource'; Code = 'unsafe func main(): OwnedFile { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; }'; Error = 'S191' },
    @{ Name = 'native_owned_return'; Code = 'extern other("stdio.h") { func invalid(): OwnedFile; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'unsafe_factory_requires_unsafe'; Code = 'unsafe ' + $factory + 'func main(): none { var owner: OwnedFile = make(); }'; Error = 'S164' },
    @{ Name = 'global_producer'; Code = $factory + 'var owner: OwnedFile = make(); func main(): none {}'; Error = 'S191' },
    @{ Name = 'borrow_return_still_disabled'; Code = 'func escape(value: &OwnedFile): &OwnedFile { return &value; } func main(): none {}'; Error = 'S173' }
)

try {
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_factory.sg') -Value ($contract + $factory)
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $prefix = if ($case.NoPrefix) { '' } else { $contract }
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful execution.`n$text"
        }
    }
    Write-Output "Native resource return checks passed: $($cases.Count) cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-returns-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-return test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
