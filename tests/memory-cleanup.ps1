param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-drop-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/ownership-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/ownership-cleanup.sg') -Raw
    $source = Join-Path $probeDir 'cleanup.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($exitCode -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 25) {
        throw "Cleanup runtime checks failed ($passes/25 PASS).`n$text"
    }
    Write-Output "Native cleanup checks passed: $passes assertions."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
