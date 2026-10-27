param([Parameter(Mandatory = $true)][string]$CompilerDll)

# A project created by 'new' must build and run without a local std copy: the
# distributed std beside the compiler is the fallback import location.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-cli-new-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null

try {
    if (!(Test-Path -LiteralPath (Join-Path (Split-Path $compilerPath) 'std\console.sg'))) {
        throw 'The compiler output must include the distributed std sources.'
    }
    Push-Location $probeDir
    try { $created = & dotnet $compilerPath new demo 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "'new' should succeed.`n$($created -join "`n")" }
    $projectDir = Join-Path $probeDir 'demo'
    if (Test-Path -LiteralPath (Join-Path $projectDir 'std')) { throw "'new' should not need a local std copy." }

    Push-Location $projectDir
    try { $output = & dotnet $compilerPath run 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "A new project should run with the distributed std.`n$($output -join "`n")" }
    Write-Output 'New project checks passed: created, built and ran with the distributed std.'
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-cli-new-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside CLI test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
