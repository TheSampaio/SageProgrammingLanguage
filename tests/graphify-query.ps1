param([Parameter(Mandatory = $true)][string]$CompilerDll)

# Validates the Graphify query CLI: focused compact JSON answers, human mode, stale fallback, and usage errors.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-graphify-query-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'

function Invoke-Sage([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue' # Usage errors go to stderr, which Windows PowerShell turns into errors.
    try { $lines = @(& dotnet $compilerPath @Arguments 2>$null | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $previous }
    [pscustomobject]@{ Exit = $LASTEXITCODE; Lines = $lines; Text = ($lines -join "`n") }
}
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-Query([string]$Kind, [string]$Name, [string[]]$Extra = @()) {
    $result = Invoke-Sage (@('graphify', 'query', $Kind, $Name, '--project', $probeDir) + $Extra)
    Assert-True ($result.Exit -eq 0) "query $Kind $Name failed with $($result.Exit): $($result.Text)"
    Assert-True ($result.Lines.Count -eq 1) "Query output must be one compact JSON line, got $($result.Lines.Count) lines."
    $result.Text | ConvertFrom-Json
}
function Same($left, $right) { (@($left) -join ',') -eq (@($right) -join ',') }

try {
    New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'tests/fixtures/graphify/*.sg') -Destination $sourceDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Sandbox/std/console.sg') -Destination $stdDir
    $mainPath = Join-Path $sourceDir 'main.sg'

    # The first query builds the index on demand; nothing else is printed.
    $r = Get-Query symbol 'geometry::add'
    Assert-True ($r.query -eq 'symbol' -and $r.name -eq 'geometry::add' -and $r.count -eq 1) 'The envelope reports query, name, and count.'
    Assert-True ($r.results[0].kind -eq 'function' -and $r.results[0].file -eq 'src/geometry.sg' -and $r.results[0].line -eq 3 -and
        $r.results[0].detail -eq 'func add(a: i32, b: i32): i32') 'symbol must locate the function with its signature.'
    Assert-True ((Get-Query symbol 'add').count -eq 1) 'An unqualified name must match the namespaced function.'
    $r = Get-Query symbol 'Point'
    Assert-True ($r.results[0].kind -eq 'struct' -and $r.results[0].detail -eq 'copy') 'Structs report their copy capability.'
    $r = Get-Query symbol 'OwnedFile'
    Assert-True ($r.results[0].kind -eq 'resource' -and $r.results[0].detail -match 'native::fclose') 'Resources report their destructor.'
    $r = Get-Query symbol 'Point.x'
    Assert-True ($r.results[0].kind -eq 'field' -and $r.results[0].line -eq 1 -and $r.results[0].detail -eq 'i32') 'Fields are addressed as Struct.field.'
    Assert-True ((Get-Query symbol 'files').results[0].kind -eq 'module') 'Module names are symbols.'
    $r = Get-Query symbol 'absent_symbol'
    Assert-True ($r.count -eq 0 -and @($r.results).Count -eq 0) 'Unknown symbols yield an empty answer.'

    $r = Get-Query callers 'geometry::shift'
    Assert-True ($r.count -eq 1 -and $r.results[0].caller -eq 'main' -and $r.results[0].file -eq 'src/main.sg' -and $r.results[0].line -eq 6) 'callers must resolve call sites.'
    $r = Get-Query callees 'main'
    $callees = @($r.results | ForEach-Object { $_.callee })
    Assert-True (Same ($callees | Sort-Object) @('console::print_line', 'files::release', 'geometry::norm', 'geometry::shift')) "callees: $($callees -join ',')"
    Assert-True (($r.results | Where-Object { $_.callee -eq 'geometry::shift' }).origin -eq 'geometry') 'Calls report the declaring module.'
    $r = Get-Query callees 'geometry::shift'
    Assert-True ($r.count -eq 1 -and $r.results[0].callee -eq 'geometry::add' -and $r.results[0].sites[0].line -eq 4) 'callees must group call sites.'

    $r = Get-Query refs 'geometry::add'
    Assert-True ($r.count -eq 1 -and $r.results[0].in -eq 'geometry::shift' -and $r.results[0].kind -eq 'call') 'refs must find the call inside shift.'
    Assert-True ((Get-Query refs 'Point.x').count -ge 2) 'Field uses are references.'
    $r = Get-Query refs 'Point'
    Assert-True ($null -ne ($r.results | Where-Object { $_.kind -eq 'type' -and $_.file -eq 'src/main.sg' })) 'Type uses are references.'

    $r = Get-Query deps 'main'
    Assert-True ((Same $r.results[0].uses @('console', 'files', 'geometry')) -and (Same $r.results[0].transitive @('console', 'files', 'geometry'))) 'deps lists direct and transitive modules.'
    Assert-True (Same (Get-Query deps 'geometry').results[0].usedBy @('main')) 'deps lists reverse dependencies.'
    Assert-True ((Get-Query deps 'nothing').count -eq 0) 'Unknown modules yield an empty answer.'

    $r = Get-Query contract 'geometry::shift'
    $params = $r.results[0].parameters
    Assert-True ($params[0].contract -eq 'borrow_mut' -and $params[1].contract -eq 'copy' -and -not $r.results[0].unsafe) 'contract reports mutable borrows and copies.'
    Assert-True ((Get-Query contract 'geometry::norm').results[0].parameters[0].contract -eq 'borrow') 'contract reports shared borrows.'
    $r = Get-Query contract 'files::release'
    Assert-True ($r.results[0].parameters[0].contract -eq 'consume' -and $r.results[0].resources[0].name -eq 'OwnedFile') 'contract reports consuming resource parameters.'
    $r = Get-Query contract 'native::fclose'
    Assert-True ($r.results[0].extern -and $r.results[0].unsafe) 'Native functions are extern and unsafe.'
    Assert-True ((Get-Query contract 'main').results[0].unsafe) 'unsafe functions are flagged.'

    $r = Get-Query module 'geometry'
    Assert-True ($r.results[0].file -eq 'src/geometry.sg' -and @($r.results[0].functions).Count -eq 3 -and $r.results[0].structs[0].name -eq 'Point') 'module summarizes declarations.'
    Assert-True ((Get-Query module 'files').results[0].resources[0].destructor -eq 'native::fclose') 'module summary includes resources.'

    # Answers stay focused: far smaller than the shard files they come from.
    $answer = Invoke-Sage @('graphify', 'query', 'callers', 'geometry::shift', '--project', $probeDir)
    Assert-True ($answer.Text.Length -lt 400) 'A focused answer must stay compact.'

    $human = Invoke-Sage @('graphify', 'query', 'callers', 'geometry::shift', '--project', $probeDir, '--human')
    Assert-True ($human.Exit -eq 0 -and $human.Text -match 'main -> geometry::shift' -and $human.Text -notmatch '^\{') 'Human mode prints text.'
    $human = Invoke-Sage @('graphify', 'query', 'contract', 'geometry::shift', '--project', $probeDir, '--human')
    Assert-True ($human.Text -match 'p: &mut Point\s+\[borrow_mut\]') 'Human contract mode shows the borrow contract.'

    # Usage errors.
    Assert-True ((Invoke-Sage @('graphify', 'query', 'bogus', 'x', '--project', $probeDir)).Exit -eq 1) 'Unknown query kinds fail.'
    Assert-True ((Invoke-Sage @('graphify', 'query', 'symbol', '--project', $probeDir)).Exit -eq 1) 'A name is required.'
    Assert-True ((Invoke-Sage @('graphify', 'query', 'symbol', 'x', '--wat')).Exit -eq 1) 'Unknown options fail.'

    # Queries refresh the index after edits, so answers match the sources.
    Add-Content -LiteralPath $mainPath -Value 'func helper2(): none {}'
    Assert-True ((Get-Query symbol 'helper2').count -eq 1) 'Queries must reflect edited sources.'

    # Stale fallback when the project no longer compiles; --no-refresh uses the persisted index as is.
    Set-Content -LiteralPath $mainPath -Value 'func main(): none { var x = ; }'
    $stale = Get-Query callers 'geometry::shift'
    Assert-True ($stale.stale -and $stale.count -eq 1) 'A broken project answers from the last good index, flagged stale.'
    $fallback = Invoke-Sage @('graphify', 'query', 'symbol', 'main', '--project', $probeDir, '--no-refresh')
    Assert-True ($fallback.Exit -eq 0 -and -not ($fallback.Text | ConvertFrom-Json).stale) '--no-refresh answers from the persisted index.'

    # A project that was never indexed has nothing to answer with --no-refresh, and fails to build when broken.
    $fresh = Join-Path $probeDir 'fresh'
    New-Item -ItemType Directory -Path (Join-Path $fresh 'src') | Out-Null
    Set-Content -LiteralPath (Join-Path $fresh 'src/main.sg') -Value 'func main(): none {}'
    Assert-True ((Invoke-Sage @('graphify', 'query', 'symbol', 'main', '--project', $fresh, '--no-refresh')).Exit -eq 1) 'Missing indexes fail with --no-refresh.'
    Set-Content -LiteralPath (Join-Path $fresh 'src/main.sg') -Value 'func main(): none { missing(); }'
    $broken = Invoke-Sage @('graphify', 'query', 'symbol', 'main', '--project', $fresh)
    Assert-True ($broken.Exit -eq 1 -and $broken.Text -match 'error S105:') 'A broken, never-indexed project reports its diagnostic.'

    Write-Output 'Graphify query checks passed.'
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
