param([Parameter(Mandatory = $true)][string]$CompilerDll)

# Validates the Graphify semantic index build: compiler-derived shards, determinism, incremental
# invalidation, silent success, compile-error reporting, and unchanged normal builds.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-graphify-index-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
$indexDir = Join-Path $probeDir 'obj/graphify'

function Invoke-Sage([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue' # Usage errors go to stderr, which Windows PowerShell turns into errors.
    try { $lines = @(& dotnet $compilerPath @Arguments 2>$null | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $previous }
    [pscustomobject]@{ Exit = $LASTEXITCODE; Lines = $lines; Text = ($lines -join "`n") }
}
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-IndexHashes {
    Get-ChildItem -LiteralPath $indexDir -Recurse -File | Sort-Object FullName |
        ForEach-Object { "$($_.FullName.Substring($indexDir.Length)):$((Get-FileHash -LiteralPath $_.FullName).Hash)" }
}
function Build-Stats {
    $result = Invoke-Sage @('graphify', 'build', $probeDir, '--stats')
    Assert-True ($result.Exit -eq 0) "build failed: $($result.Text)"
    $result.Text | ConvertFrom-Json
}
function Same($left, $right) { (@($left) -join ',') -eq (@($right) -join ',') }
function Read-Shard([string]$Module) { Get-Content -LiteralPath (Join-Path $indexDir "modules/$Module.json") -Raw | ConvertFrom-Json }

try {
    New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'tests/fixtures/graphify/*.sg') -Destination $sourceDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Sandbox/std/console.sg') -Destination $stdDir
    $mainPath = Join-Path $sourceDir 'main.sg'

    # Silent build; one shard per module plus a manifest; no code generation.
    $build = Invoke-Sage @('graphify', 'build', $probeDir)
    Assert-True ($build.Exit -eq 0 -and $build.Text.Length -eq 0) "Build must be silent on success: $($build.Text)"
    foreach ($module in 'main', 'geometry', 'files', 'console') {
        Assert-True (Test-Path -LiteralPath (Join-Path $indexDir "modules/$module.json")) "Missing shard for $module."
    }
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $probeDir 'obj/main.c'))) 'Indexing must not generate C.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $probeDir 'bin/main.exe'))) 'Indexing must not link binaries.'
    $manifestText = Get-Content -LiteralPath (Join-Path $indexDir 'manifest.json') -Raw
    $manifest = $manifestText | ConvertFrom-Json
    Assert-True ($manifest.entry -eq 'src/main.sg' -and @($manifest.modules).Count -eq 4 -and $manifest.compiler -match '^\d+\.\d+\.\d+$') 'Manifest must list the entry, compiler, and four modules.'
    Assert-True ($manifestText -notmatch [regex]::Escape($probeDir.Replace('\', '\\'))) 'The index must not contain machine paths.'

    # Shard content comes from compiler analysis: resolved call origins, contracts, and capabilities.
    $main = Read-Shard 'main'
    $shift = $main.references | Where-Object { $_.name -eq 'geometry::shift' }
    Assert-True ($shift.kind -eq 'call' -and $shift.in -eq 'main' -and $shift.origin -eq 'geometry' -and $shift.line -eq 6) 'Calls must carry the resolved callee origin.'
    Assert-True (@($main.uses | ForEach-Object { $_.module }).Count -eq 3) 'Shards list use dependencies.'
    $geometry = Read-Shard 'geometry'
    $parameters = ($geometry.functions | Where-Object { $_.qualified -eq 'geometry::shift' }).parameters
    Assert-True ($parameters[0].contract -eq 'borrow_mut' -and $parameters[1].contract -eq 'copy') 'Parameters must carry their borrow contract.'
    Assert-True ($geometry.structs[0].name -eq 'Point' -and $geometry.structs[0].copy -and -not $geometry.structs[0].resource) 'Structs must carry copy capability.'
    Assert-True ($geometry.structs[0].fields[0].line -eq 1 -and $geometry.structs[0].fields[0].column -eq 16) 'Struct fields must be located.'
    $files = Read-Shard 'files'
    Assert-True ($files.resources[0].name -eq 'OwnedFile' -and $files.resources[0].destructor -eq 'native::fclose') 'Native resources must be indexed.'
    Assert-True (($files.functions | Where-Object { $_.qualified -eq 'files::release' }).parameters[0].contract -eq 'consume') 'Consuming resource parameters must be marked.'
    $console = Read-Shard 'console'
    Assert-True ($console.origin -eq 'std' -and $console.file -eq 'std/console.sg') 'Standard-library modules are flagged and located portably.'

    # Deterministic: rebuilding from scratch is byte-identical; a repeated build rewrites nothing.
    $first = Get-IndexHashes
    Remove-Item -LiteralPath $indexDir -Recurse -Force
    Assert-True ((Invoke-Sage @('graphify', 'build', $probeDir)).Exit -eq 0) 'Rebuild failed.'
    Assert-True (Same $first (Get-IndexHashes)) 'Two index builds must be byte-identical.'
    $stats = Build-Stats
    Assert-True ($stats.modules -eq 4 -and @($stats.written).Count -eq 0 -and @($stats.unchanged).Count -eq 4) 'An unchanged project must rewrite no shard.'

    # Incremental invalidation: only the changed module and its dependents are rewritten.
    Add-Content -LiteralPath (Join-Path $sourceDir 'geometry.sg') -Value '// touched'
    $stats = Build-Stats
    Assert-True (Same (@($stats.written) | Sort-Object) @('geometry', 'main')) "Changing geometry must rewrite geometry and main, got: $(@($stats.written) -join ',')"
    Assert-True (Same (@($stats.unchanged) | Sort-Object) @('console', 'files')) 'Independent modules must stay untouched.'
    Add-Content -LiteralPath $mainPath -Value '// touched'
    Assert-True (Same @((Build-Stats).written) @('main')) 'Changing a leaf module must rewrite only that shard.'
    Add-Content -LiteralPath (Join-Path $stdDir 'console.sg') -Value '// touched'
    Assert-True (Same (@((Build-Stats).written) | Sort-Object) @('console', 'main')) 'Changing a std module must rewrite it and its dependents.'

    # Removing a module deletes its shard.
    Remove-Item -LiteralPath (Join-Path $sourceDir 'files.sg')
    Set-Content -LiteralPath $mainPath -Value 'use geometry; func main(): none { var p: Point = { x = 1, y = 2 }; geometry::shift(&mut p, 3); }'
    $stats = Build-Stats
    Assert-True (Same (@($stats.removed) | Sort-Object) @('console', 'files')) "Orphan shards must be removed, got: $(@($stats.removed) -join ',')"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $indexDir 'modules/files.json'))) 'The removed module shard must be deleted.'

    # Compile errors use the normal diagnostics and leave the previous index intact.
    $before = Get-IndexHashes
    Set-Content -LiteralPath $mainPath -Value 'func main(): none { var x = ; }'
    $failed = Invoke-Sage @('graphify', 'build', $probeDir)
    Assert-True ($failed.Exit -eq 1 -and $failed.Text -match 'main\.sg\(\d+,\d+\): error S00\d:') 'Build must report compile errors in the standard format.'
    $failedJson = Invoke-Sage @('graphify', 'build', $probeDir, '--diagnostics', 'json')
    Assert-True ($failedJson.Exit -eq 1 -and ($failedJson.Lines[0] | ConvertFrom-Json).code -match '^S00') 'Build errors must honor --diagnostics json.'
    Assert-True (Same $before (Get-IndexHashes)) 'A failed build must not modify the index.'
    Assert-True ((Invoke-Sage @('graphify', 'build', (Join-Path $probeDir 'nowhere'))).Exit -eq 1) 'A missing project must fail.'
    Assert-True ((Invoke-Sage @('graphify', 'frobnicate')).Exit -eq 1) 'Unknown graphify commands must fail.'

    # Normal compilation is unaffected by the index.
    Set-Content -LiteralPath $mainPath -Value 'func main(): none {}'
    Push-Location $probeDir
    try {
        $normal = Invoke-Sage @('build')
        Assert-True ($normal.Exit -eq 0 -and $normal.Text -match 'Build complete') 'Regular builds must keep their output.'
    }
    finally { Pop-Location }

    Write-Output 'Graphify index checks passed.'
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
