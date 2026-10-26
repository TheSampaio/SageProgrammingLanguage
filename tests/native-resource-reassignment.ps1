param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-reassignment-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$owners = 'var first: OwnedFile = make(); var second: OwnedFile = make(); '
$cases = @(
    @{ Name = 'self_assignment'; Code = $owners + 'first = first;'; Error = 'S143' },
    @{ Name = 'use_after_reassignment'; Code = $owners + 'first = second; second;'; Error = 'S140' },
    @{ Name = 'reassign_moved_destination'; Code = $owners + 'var moved: OwnedFile = first; first = second;'; Error = 'S140' },
    @{ Name = 'reassign_const'; Code = 'const first: OwnedFile = make(); var second: OwnedFile = make(); first = second;'; Error = 'S103' },
    @{ Name = 'reassign_during_borrow'; Code = $owners + 'var view: &OwnedFile = &first; first = second;'; Error = 'S178' },
    @{ Name = 'move_borrowed_source'; Code = $owners + 'var view: &OwnedFile = &second; first = view;'; Error = 'S191' },
    @{ Name = 'move_source_with_live_borrow'; Code = $owners + 'var view: &OwnedFile = &second; first = second;'; Error = 'S178' },
    @{ Name = 'reject_anonymous_source'; Code = $owners + 'first = make();'; Error = 'S143' },
    @{ Name = 'reject_nested_result'; Code = $owners + 'var third: OwnedFile = (first = second);'; Error = 'S192' }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-reassignment.sg') -Raw
    $source = Join-Path $probeDir 'resource_reassignment.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 9) {
        throw "Resource reassignment runtime checks failed ($passes/9 PASS).`n$text"
    }

    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ($contract + $factory + 'func main(): none { ' + $case.Code + ' }')
        $output = & dotnet $compilerPath $source 2>&1
        $text = $output -join "`n"
        if ($LASTEXITCODE -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
            throw "$($case.Name): expected semantic $($case.Error) before C generation.`n$text"
        }
    }
    Write-Output "Native resource reassignment checks passed: $passes runtime assertions and $($cases.Count) rejected programs."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-reassignment-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-reassignment test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
