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

    $sourceDir = Join-Path $probeDir 'src'
    New-Item -ItemType Directory -Path $sourceDir | Out-Null
    $runtimeSource = Join-Path $sourceDir 'main.sg'
    Set-Content -LiteralPath $runtimeSource -Value 'extern native("stdlib.h") { func exit(code: c_int): none; } func main(): none { native::exit(37); }'
    $output = & dotnet $compilerPath $runtimeSource 2>&1
    if ($LASTEXITCODE -ne 37) {
        throw "Explicit-file execution must propagate native exit 37, received $LASTEXITCODE.`n$output"
    }
    Push-Location $probeDir
    try {
        $output = & dotnet $compilerPath build 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Build-only must succeed without executing the application.`n$output" }
        $output = & dotnet $compilerPath run 2>&1
        if ($LASTEXITCODE -ne 37) { throw "Run must propagate native exit 37, received $LASTEXITCODE.`n$output" }
    }
    finally { Pop-Location }

    Set-Content -LiteralPath $runtimeSource -Value 'func main(): none {}'
    $output = & dotnet $compilerPath $runtimeSource 2>&1
    if ($LASTEXITCODE -ne 0) { throw "A successful application must return zero.`n$output" }

    Write-Output 'CLI exit-code checks passed.'
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
