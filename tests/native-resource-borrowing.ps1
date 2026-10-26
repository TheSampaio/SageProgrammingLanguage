param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-borrows-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$owner = 'var owner: OwnedFile = (0 as FILE*) as OwnedFile; '
$reader = 'func empty(value: &OwnedFile): b8 { unsafe { return (value as FILE*) == (0 as FILE*); } } '
$cases = @(
    @{ Name = 'shared_parameter'; Code = $reader + 'unsafe func main(): none { ' + $owner + 'var result: b8 = empty(&owner); }' },
    @{ Name = 'mutable_parameter'; Code = 'func visit(value: &mut OwnedFile): none {} unsafe func main(): none { ' + $owner + 'visit(&mut owner); }' },
    @{ Name = 'shared_local'; Code = $reader + 'unsafe func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; var result: b8 = empty(&view); }' },
    @{ Name = 'mutable_local'; Code = 'unsafe func main(): none { ' + $owner + 'var view: &mut OwnedFile = &mut owner; var raw: FILE* = view as FILE*; }' },
    @{ Name = 'move_after_loan'; Code = 'unsafe func main(): none { ' + $owner + 'if (true) { var view: &OwnedFile = &owner; } var next: OwnedFile = owner; }' },
    @{ Name = 'shared_const_owner'; Code = $reader + 'unsafe func main(): none { const owner: OwnedFile = (0 as FILE*) as OwnedFile; var result: b8 = empty(&owner); }' },
    @{ Name = 'shadow_borrow'; Code = $reader + 'unsafe func main(): none { ' + $owner + 'if (true) { var owner: &OwnedFile = &owner; var result: b8 = empty(&owner); } var next: OwnedFile = owner; }' },
    @{ Name = 'forward_borrow_signature'; Code = $reader + $contract + 'unsafe func main(): none { ' + $owner + 'var result: b8 = empty(&owner); }'; NoPrefix = $true },
    @{ Name = 'reborrow'; Code = 'unsafe func main(): none { ' + $owner + 'var parent: &mut OwnedFile = &mut owner; if (true) { var child: &mut OwnedFile = &mut parent; var raw: FILE* = child as FILE*; } var raw: FILE* = parent as FILE*; }' },
    @{ Name = 'move_shared_loan'; Code = 'unsafe func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; var next: OwnedFile = owner; }'; Error = 'S178' },
    @{ Name = 'read_mutable_loan'; Code = 'unsafe func main(): none { ' + $owner + 'var view: &mut OwnedFile = &mut owner; owner; }'; Error = 'S178' },
    @{ Name = 'move_from_shared'; Code = 'func steal(value: &OwnedFile): none { var next: OwnedFile = value; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'move_from_mutable'; Code = 'func steal(value: &mut OwnedFile): none { var next: OwnedFile = value; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'move_from_local_loan'; Code = 'unsafe func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; var next: OwnedFile = view; }'; Error = 'S191' },
    @{ Name = 'delete_borrowed_resource'; Code = 'unsafe func destroy(value: &mut OwnedFile): none { delete value; } unsafe func main(): none {}'; Error = 'S174' },
    @{ Name = 'const_mutable_borrow'; Code = 'unsafe func main(): none { const owner: OwnedFile = (0 as FILE*) as OwnedFile; var view: &mut OwnedFile = &mut owner; }'; Error = 'S171' },
    @{ Name = 'shared_upgrade'; Code = 'func upgrade(value: &OwnedFile): none { var view: &mut OwnedFile = &mut value; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'same_call_alias'; Code = 'func both(a: &mut OwnedFile, b: &OwnedFile): none {} unsafe func main(): none { ' + $owner + 'both(&mut owner, &owner); }'; Error = 'S172' },
    @{ Name = 'effectful_argument'; Code = 'func zero(): i32 { return 0; } func visit(value: &OwnedFile, x: i32): none {} unsafe func main(): none { ' + $owner + 'visit(&owner, zero()); }'; Error = 'S177' },
    @{ Name = 'missing_explicit_borrow'; Code = $reader + 'unsafe func main(): none { ' + $owner + 'empty(owner); }'; Error = 'S170' },
    @{ Name = 'native_borrow_signature'; Code = 'extern other("stdio.h") { func invalid(value: &OwnedFile): none; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'native_implicit_borrow'; Code = 'unsafe func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; native::fclose(view); }'; Error = 'S191' },
    @{ Name = 'resource_borrow_return'; Code = 'func escape(value: &OwnedFile): &OwnedFile { return &value; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'resource_borrow_array'; Code = 'func visit(value: &OwnedFile[2]): none {} func main(): none {}'; Error = 'S191' },
    @{ Name = 'resource_borrow_pointer'; Code = 'func visit(value: &OwnedFile*): none {} func main(): none {}'; Error = 'S191' },
    @{ Name = 'parent_suspended'; Code = 'unsafe func main(): none { ' + $owner + 'var parent: &mut OwnedFile = &mut owner; var child: &mut OwnedFile = &mut parent; parent; }'; Error = 'S178' },
    @{ Name = 'global_resource_loan'; Code = 'var view: &OwnedFile = &missing; func main(): none {}'; Error = 'S191' },
    @{ Name = 'borrow_moved_owner'; Code = $reader + 'unsafe func main(): none { ' + $owner + 'var moved: OwnedFile = owner; empty(&owner); }'; Error = 'S140' },
    @{ Name = 'module_forward_signature'; Code = 'module handles { ' + $reader + $contract + ' } unsafe func main(): none { ' + $owner + 'var result: b8 = handles::empty(&owner); }'; NoPrefix = $true },
    @{ Name = 'imported_borrow'; Code = 'use resource_import; unsafe func main(): none { ' + $owner + 'var result: b8 = empty(&owner); }'; NoPrefix = $true },
    @{ Name = 'temporary_resource_borrow'; Code = $reader + 'unsafe func main(): none { empty(&((0 as FILE*) as OwnedFile)); }'; Error = 'S175' },
    @{ Name = 'shared_resource_overwrite'; Code = 'func overwrite(value: &OwnedFile): none { value = value; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'mutable_resource_overwrite'; Code = 'func overwrite(value: &mut OwnedFile): none { value = value; } func main(): none {}'; Error = 'S143' }
)

try {
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_import.sg') -Value ($contract + $reader)
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
    Write-Output "Native resource borrow checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
