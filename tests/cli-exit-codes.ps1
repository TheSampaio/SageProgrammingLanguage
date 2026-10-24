param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-cli-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null

try {
    $versionOutput = & dotnet $compilerPath --version 2>&1
    if ($LASTEXITCODE -ne 0 -or ($versionOutput -join "`n") -notmatch 'Sage Compiler v\d+\.\d+\.\d+') {
        throw "--version should succeed and report a semantic version."
    }

    $source = Join-Path $probeDir 'invalid.sg'
    Set-Content -LiteralPath $source -Value 'use absent_probe_module; func main(): none {}'
    $diagnostic = & dotnet $compilerPath $source 2>&1
    if ($LASTEXITCODE -eq 0 -or ($diagnostic -join "`n") -notmatch 'error S007:') {
        throw "An unresolved import should fail with S007 and a nonzero exit code."
    }

    Write-Output 'CLI exit-code checks passed.'
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
