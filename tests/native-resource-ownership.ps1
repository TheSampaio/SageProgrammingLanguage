param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-ownership-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$adopt = 'var owner: OwnedFile = (0 as FILE*) as OwnedFile; '
$cases = @(
    @{ Name = 'null_local'; Code = 'unsafe func main(): none { ' + $adopt + '}' },
    @{ Name = 'local_move'; Code = 'func main(): none { unsafe { ' + $adopt + 'var moved: OwnedFile = owner; } }' },
    @{ Name = 'unsafe_adoption_required'; Code = 'func main(): none { ' + $adopt + '}'; Error = 'S192' },
    @{ Name = 'direct_local_required'; Code = 'unsafe func main(): none { (0 as FILE*) as OwnedFile; }'; Error = 'S192' },
    @{ Name = 'anonymous_extraction'; Code = 'unsafe func main(): none { var raw: FILE* = ((0 as FILE*) as OwnedFile) as FILE*; }'; Error = 'S192' },
    @{ Name = 'adoption_wrong_pointer'; Code = 'unsafe func main(): none { var owner: OwnedFile = (0 as none*) as OwnedFile; }'; Error = 'S192' },
    @{ Name = 'missing_initializer'; Code = 'func main(): none { var owner: OwnedFile; }'; Error = 'S012' },
    @{ Name = 'global_owner'; Code = $adopt + 'func main(): none {}'; Error = 'S191' },
    @{ Name = 'move_after_move'; Code = 'unsafe func main(): none { ' + $adopt + 'var moved: OwnedFile = owner; owner; }'; Error = 'S140' },
    @{ Name = 'maybe_moved'; Code = 'unsafe func main(): none { ' + $adopt + 'if (true) { var moved: OwnedFile = owner; } owner; }'; Error = 'S142' },
    @{ Name = 'loop_outer_move'; Code = 'unsafe func main(): none { ' + $adopt + 'for (var i: i32 = 0; i < 2; i++) { var moved: OwnedFile = owner; } }'; Error = 'S145' },
    @{ Name = 'owner_overwrite'; Code = 'unsafe func main(): none { ' + $adopt + 'owner = owner; }'; Error = 'S143' },
    @{ Name = 'const_overwrite'; Code = 'unsafe func main(): none { const owner: OwnedFile = (0 as FILE*) as OwnedFile; owner = owner; }'; Error = 'S103' },
    @{ Name = 'delete_resource'; Code = 'unsafe func main(): none { ' + $adopt + 'delete owner; }'; Error = 'S132' },
    @{ Name = 'resource_comparison'; Code = 'unsafe func main(): none { ' + $adopt + 'var same: b8 = owner == owner; }'; Error = 'S191' },
    @{ Name = 'resource_interpolation'; Code = 'unsafe func main(): none { ' + $adopt + 'var text: str = $"${owner}"; }'; Error = 'S191' },
    @{ Name = 'native_argument_no_copy'; Code = 'unsafe func main(): none { ' + $adopt + 'native::fclose(owner); }'; Error = 'S191' },
    @{ Name = 'resource_cast_no_clone'; Code = 'unsafe func main(): none { ' + $adopt + 'var clone: OwnedFile = owner as OwnedFile; }'; Error = 'S192' },
    @{ Name = 'resource_cast_no_scalar'; Code = 'unsafe func main(): none { ' + $adopt + 'var value: i32 = owner as i32; }'; Error = 'S192' },
    @{ Name = 'resource_cast_no_other_pointer'; Code = 'unsafe func main(): none { ' + $adopt + 'var raw: none* = owner as none*; }'; Error = 'S192' },
    @{ Name = 'extraction_after_move'; Code = 'unsafe func main(): none { ' + $adopt + 'var moved: OwnedFile = owner; var raw: FILE* = owner as FILE*; }'; Error = 'S140' },
    @{ Name = 'local_type_shadow'; Code = 'unsafe func main(): none { var OwnedFile: i32 = 1; ' + $adopt + '}'; Error = 'S150' },
    @{ Name = 'parameter_type_shadow'; Code = 'func shadow(OwnedFile: i32): none {} func main(): none {}'; Error = 'S150' },
    @{ Name = 'owner_parameter'; Code = 'func take(value: OwnedFile): none {} func main(): none {}' },
    @{ Name = 'resource_private_handle'; Code = 'unsafe func main(): none { ' + $adopt + 'owner._sage_handle; }'; Error = 'S109' },
    @{ Name = 'parenthesized_adoption'; Code = 'unsafe func main(): none { var owner: OwnedFile = ((0 as FILE*) as OwnedFile); }' },
    @{ Name = 'returning_branch_owner_available'; Code = 'unsafe func choose(flag: b8): i32 { ' + $adopt + 'if (flag) { var moved: OwnedFile = owner; return 1; } var moved: OwnedFile = owner; return 0; } unsafe func main(): none { var value: i32 = choose(false); }' },
    @{ Name = 'both_branches_moved'; Code = 'unsafe func main(): none { ' + $adopt + 'if (true) { var first: OwnedFile = owner; } else { var second: OwnedFile = owner; } owner; }'; Error = 'S140' },
    @{ Name = 'nested_adoption'; Code = 'unsafe func main(): none { var owner: OwnedFile = (((0 as FILE*) as OwnedFile) as FILE*) as OwnedFile; }'; Error = 'S192' },
    @{ Name = 'cast_no_facade_pointer'; Code = 'unsafe func main(): none { ' + $adopt + 'owner as OwnedFile*; }'; Error = 'S191' },
    @{ Name = 'imported_owner'; Code = 'use resource_import; unsafe func main(): none { ' + $adopt + 'var moved: OwnedFile = owner; }'; NoPrefix = $true }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-cleanup.sg') -Raw
    $source = Join-Path $probeDir 'resource_cleanup.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 66) {
        throw "Native resource cleanup checks failed ($passes/66 PASS).`n$text"
    }
    $minimalHeader = Join-Path $probeDir 'minimal-resource.h'
    Set-Content -LiteralPath $minimalHeader -Value 'typedef struct MinimalHandle MinimalHandle; static void minimal_drop(MinimalHandle* handle) { (void)handle; }'
    $minimalSource = 'extern minimal("' + $minimalHeader.Replace('\', '/') + '") { type MinimalHandle; func minimal_drop(value: MinimalHandle*): none; resource MinimalOwner: MinimalHandle* drop minimal_drop; } unsafe func main(): none { var value: MinimalOwner = (0 as MinimalHandle*) as MinimalOwner; }'
    Set-Content -LiteralPath (Join-Path $probeDir 'minimal.sg') -Value $minimalSource
    $output = & dotnet $compilerPath (Join-Path $probeDir 'minimal.sg') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Resource cleanup must provide its own NULL declaration.`n$($output -join "`n")" }
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_import.sg') -Value $contract
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
    Write-Output "Native resource ownership checks passed: $passes runtime assertions and $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
