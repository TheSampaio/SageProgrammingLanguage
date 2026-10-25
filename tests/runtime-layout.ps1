param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$stdlib = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std')).Path
$foreign = @(Get-ChildItem -LiteralPath $stdlib -Recurse -Force -File | Where-Object Extension -ne '.sg')
if ($foreign.Count -ne 0) { throw "std must contain only .sg files: $($foreign.Name -join ', ')" }
foreach ($name in @('http_server', 'http_client')) {
    $modulePath = Join-Path $stdlib "$name.sg"
    if (!(Test-Path -LiteralPath $modulePath) -or (Get-Content -LiteralPath $modulePath -Raw) -notmatch "(?m)^module $name\s*$") {
        throw "Expected renamed file and module $name."
    }
}
foreach ($name in @('server.sg', 'http.sg')) {
    if (Test-Path -LiteralPath (Join-Path $stdlib $name)) { throw "Legacy filename remains: $name" }
}
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-runtime-layout-' + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $probeDir 'compiler with spaces'
$projectDir = Join-Path $probeDir 'project with spaces'
New-Item -ItemType Directory -Path $appDir, (Join-Path $projectDir 'src'), (Join-Path $projectDir 'std') | Out-Null
try {
    $compilerDir = Split-Path -Parent $compilerPath
    foreach ($name in @('Sage.dll', 'Sage.deps.json', 'Sage.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $compilerDir $name) -Destination $appDir
    }
    Copy-Item -LiteralPath (Join-Path $compilerDir 'Runtime') -Destination $appDir -Recurse
    Copy-Item -Path (Join-Path $stdlib '*.sg') -Destination (Join-Path $projectDir 'std')
    $source = Join-Path $projectDir 'src/layout.sg'
    Set-Content -LiteralPath $source -Value 'use byte_file; use http_server; use http_client; func main(): none { var owner: ByteFile = byte_file::empty(); var closed: b8 = byte_file::close(owner); }'
    $relocatedCompiler = Join-Path $appDir 'Sage.dll'
    $output = & dotnet $relocatedCompiler $source 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match 'error |\[FAIL\]|Fatal') { throw "Relocated runtime/std compile failed.`n$($output -join "`n")" }
    foreach ($oldName in @('http', 'server')) {
        $oldSource = Join-Path $projectDir "src/old_$oldName.sg"
        Set-Content -LiteralPath $oldSource -Value "use $oldName; func main(): none {}"
        $output = & dotnet $relocatedCompiler $oldSource 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch 'error S007:') { throw "Old import $oldName must report S007." }
    }
    $foreignCopies = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'std') -Recurse -Force -File | Where-Object Extension -ne '.sg')
    if ($foreignCopies.Count -ne 0) { throw 'Relocated std must remain Sage-only.' }
    Write-Output 'Runtime layout checks passed: Sage-only std, renamed HTTP modules, relocated compiler/runtime, old-import diagnostics.'
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-runtime-layout-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside layout test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
