param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-presence-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$owner = 'var owner: OwnedFile = make(); '
$cases = @(
    @{ Name = 'owner_query'; Code = 'func main(): none { ' + $owner + 'var empty: b8 = owner == null; var present: b8 = owner != null; }'; Inspect = $true },
    @{ Name = 'reverse_query'; Code = 'func main(): none { ' + $owner + 'var empty: b8 = null == owner; var present: b8 = null != owner; }' },
    @{ Name = 'shared_parameter'; Code = 'func empty(value: &OwnedFile): b8 { return value == null; } func main(): none { ' + $owner + 'var result: b8 = empty(&owner); }' },
    @{ Name = 'mutable_parameter'; Code = 'func empty(value: &mut OwnedFile): b8 { return value == null; } func main(): none { ' + $owner + 'var result: b8 = empty(&mut owner); }' },
    @{ Name = 'lexical_borrow'; Code = 'func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; var result: b8 = view == null && owner == null; }' },
    @{ Name = 'owning_parameter'; Code = 'func empty(value: OwnedFile): b8 { return value == null; } func main(): none { ' + $owner + 'var result: b8 = empty(owner); }' },
    @{ Name = 'branch_condition'; Code = 'func main(): none { ' + $owner + 'if (owner == null) { var moved: OwnedFile = owner; } }' },
    @{ Name = 'bound_query_before_consume'; Code = 'func consume(value: OwnedFile, empty: b8): none {} func main(): none { ' + $owner + 'var empty: b8 = owner == null; consume(owner, empty); }' },
    @{ Name = 'query_with_borrowed_call'; Code = 'func visit(value: &OwnedFile, empty: b8): none {} func main(): none { ' + $owner + 'visit(&owner, owner == null); }' },
    @{ Name = 'shadowed_null_identifier'; Code = 'func main(): none { var null: i32 = 1; var result: b8 = null == 1; }' },
    @{ Name = 'scalar_null_query'; Code = 'func main(): none { var result: b8 = 1 == null; }'; Error = 'S195' },
    @{ Name = 'two_nulls'; Code = 'func main(): none { var result: b8 = null == null; }'; Error = 'S195' },
    @{ Name = 'ordered_null_query'; Code = 'func main(): none { ' + $owner + 'var result: b8 = owner < null; }'; Error = 'S195' },
    @{ Name = 'arithmetic_null'; Code = 'func main(): none { ' + $owner + 'owner + null; }'; Error = 'S195' },
    @{ Name = 'two_resources_still_disabled'; Code = 'func main(): none { ' + $owner + 'var result: b8 = owner == owner; }'; Error = 'S191' },
    @{ Name = 'anonymous_result'; Code = 'func main(): none { var result: b8 = make() == null; }'; Error = 'S193' },
    @{ Name = 'moved_owner'; Code = 'func main(): none { ' + $owner + 'var moved: OwnedFile = owner; var result: b8 = owner == null; }'; Error = 'S140' },
    @{ Name = 'owner_under_mutable_loan'; Code = 'func main(): none { ' + $owner + 'var view: &mut OwnedFile = &mut owner; var result: b8 = owner == null; }'; Error = 'S178' },
    @{ Name = 'query_after_consume_argument'; Code = 'func consume(value: OwnedFile, empty: b8): none {} func main(): none { ' + $owner + 'consume(owner, owner == null); }'; Error = 'S194' },
    @{ Name = 'query_before_consume_argument'; Code = 'func consume(empty: b8, value: OwnedFile): none {} func main(): none { ' + $owner + 'consume(owner == null, owner); }'; Error = 'S194' },
    @{ Name = 'nested_query_argument'; Code = 'struct Flag { empty: b8; } func consume(flag: Flag, value: OwnedFile): none {} func main(): none { ' + $owner + 'consume({ empty = owner == null }, owner); }'; Error = 'S194' },
    @{ Name = 'raw_pointer_query_not_enabled'; Code = 'unsafe func main(): none { var value: FILE* = 0 as FILE*; var result: b8 = value == null; }'; Error = 'S195' },
    @{ Name = 'factory_symbol_not_owner'; Code = 'func main(): none { var result: b8 = make == null; }'; Error = 'S195' },
    @{ Name = 'shadowed_null_resource'; Code = 'func main(): none { var null: OwnedFile = make(); var result: b8 = null == null; }'; Error = 'S191' }
)
try {
    $minimalHeader = Join-Path $probeDir 'minimal-resource.h'
    Set-Content -LiteralPath $minimalHeader -Value 'typedef struct MinimalHandle MinimalHandle; static void minimal_drop(MinimalHandle* value) { (void)value; }'
    $minimalCode = 'extern minimal("' + $minimalHeader.Replace('\', '/') + '") { type MinimalHandle; func minimal_drop(value: MinimalHandle*): none; resource MinimalOwner: MinimalHandle* drop minimal_drop; } func empty(value: &MinimalOwner): b8 { return value == null; } func main(): none {}'
    Set-Content -LiteralPath (Join-Path $probeDir 'minimal.sg') -Value $minimalCode
    $minimalOutput = & dotnet $compilerPath (Join-Path $probeDir 'minimal.sg') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Presence query must supply NULL without another stddef dependency.`n$($minimalOutput -join "`n")" }
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
        $output = if ($case.Inspect) { & dotnet $compilerPath --verbose $source 2>&1 } else { & dotnet $compilerPath $source 2>&1 }
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
        if ($case.Inspect) {
            $ast = Get-Content -LiteralPath (Join-Path $probeDir "obj/$($case.Name).ast") -Raw
            if ($ast -notmatch '"IsResourceNullComparison": true') { throw 'AST must identify validated resource presence comparisons.' }
        }
    }
    Write-Output "Native resource presence checks passed: $($cases.Count) cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-presence-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside presence test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
