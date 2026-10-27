param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll,
    [int]$Modules = 40,
    [int]$FunctionsPerModule = 20,
    [int]$Iterations = 5,
    [string]$OutputJson
)

# Reproducible clean vs incremental build benchmark for the Sage compiler.
# Generates a synthetic multi-module project in a GUID temp directory and times `sage build`
# (wall clock, including .NET startup) for four scenarios per iteration:
#   clean       - obj/ and bin/ deleted first (works with compilers that predate --clean)
#   no-change   - rebuild with no source change
#   body        - one function body changed in a module that many modules use
#   interface   - one public function added to that module (its dependents recompile)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/bench-incremental.ps1 -CompilerDll bin/Release/net8.0/Sage.dll
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-bench-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Get-ModuleName([int]$Index) { 'm{0:D3}' -f $Index }

# Module i uses module i-1 and module i/2, so early modules have many transitive dependents.
function Write-Module([int]$Index, [int]$BodyVariant = 0, [bool]$ExtraFunction = $false) {
    $name = Get-ModuleName $Index
    $uses = @()
    if ($Index -gt 0) { $uses += Get-ModuleName ($Index - 1) }
    if ($Index -gt 1 -and [int][math]::Floor($Index / 2) -ne $Index - 1) { $uses += Get-ModuleName ([int][math]::Floor($Index / 2)) }
    $lines = @($uses | ForEach-Object { "use $_;" })
    $lines += "module $name {"
    for ($f = 0; $f -lt $FunctionsPerModule; $f++) {
        $call = if ($uses.Count -gt 0) { "$($uses[0])::f0(n)" } else { '1' }
        $lines += "    func f${f}(n: i32): i32 { var total: i32 = $call; for (var i: i32 = 0; i < n; i = i + 1) { total = total + i * $($f + 1 + $BodyVariant); } if (total > 1000) { total = total - 7; } return total; }"
    }
    if ($ExtraFunction) { $lines += '    func extra(): i32 { return 1; }' }
    $lines += '}'
    Set-Content -LiteralPath (Join-Path $sourceDir "$name.sg") -Value $lines
}

# Returns wall time; an Instrumented compiler (-c Instrumented) also reports how many units compiled.
function Invoke-TimedBuild {
    Push-Location $probeDir
    $ErrorActionPreference = 'Continue'   # instrumentation is written to stderr
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $output = & dotnet $compilerPath build 2>&1 | ForEach-Object { "$_" }
        $watch.Stop()
        if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed.`n$($output -join "`n")" }
        $units = [regex]::Match(($output -join "`n"), 'units_compiled=(\d+)')
        return [pscustomobject]@{ Ms = $watch.Elapsed.TotalMilliseconds; Units = $(if ($units.Success) { $units.Groups[1].Value } else { '' }) }
    }
    finally { Pop-Location }
}

function Get-Stats([double[]]$Values) {
    $sorted = $Values | Sort-Object
    $median = if ($sorted.Count % 2) { $sorted[[int][math]::Floor($sorted.Count / 2)] } else { ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 }
    [pscustomobject]@{ MedianMs = [math]::Round($median, 1); MinMs = [math]::Round($sorted[0], 1); MaxMs = [math]::Round($sorted[-1], 1) }
}

try {
    $hot = [int][math]::Floor($Modules / 4)   # a module with many transitive dependents
    for ($i = 0; $i -lt $Modules; $i++) { Write-Module $i }
    $mainUses = (0..($Modules - 1) | ForEach-Object { "use $(Get-ModuleName $_);" }) -join ' '
    $mainSum = (0..($Modules - 1) | ForEach-Object { "$(Get-ModuleName $_)::f0(3)" }) -join ' + '
    Set-Content -LiteralPath (Join-Path $sourceDir 'main.sg') -Value "$mainUses func main(): none { var total: i32 = $mainSum; }"

    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $configuration = if ($compilerPath -match '\\bin\\([^\\]+)\\') { $Matches[1] } else { 'unknown' }
    $environment = [ordered]@{
        Date = (Get-Date).ToString('yyyy-MM-dd HH:mm')
        SageCompiler = ((& dotnet $compilerPath --version) -join ' ').Trim()
        CompilerConfiguration = $configuration
        Gcc = "$((& gcc -dumpfullversion) -join '') $((& gcc -dumpmachine) -join '')"
        DotNet = ((& dotnet --version) -join '').Trim()
        Os = [Environment]::OSVersion.VersionString
        Cpu = $cpu.Name.Trim()
        Cores = "$($cpu.NumberOfCores) cores / $($cpu.NumberOfLogicalProcessors) threads"
        RamGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
        Workload = "$($Modules + 1) modules ($Modules generated + main), $FunctionsPerModule functions each; hot module $(Get-ModuleName $hot)"
        Iterations = $Iterations
    }

    $samples = @{ clean = @(); 'no-change' = @(); body = @(); interface = @() }
    $units = @{}
    Invoke-TimedBuild | Out-Null   # warm-up (JIT, file cache); not recorded
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        Write-Module $hot
        foreach ($dir in 'obj', 'bin') { $path = Join-Path $probeDir $dir; if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
        foreach ($scenario in 'clean', 'no-change', 'body', 'interface') {
            if ($scenario -eq 'body') { Write-Module $hot -BodyVariant $iteration }
            if ($scenario -eq 'interface') { Write-Module $hot -BodyVariant $iteration -ExtraFunction $true }
            $sample = Invoke-TimedBuild
            $samples[$scenario] += $sample.Ms
            $units[$scenario] = $sample.Units
        }
    }

    $results = foreach ($scenario in 'clean', 'no-change', 'body', 'interface') {
        $stats = Get-Stats $samples[$scenario]
        [pscustomobject]@{ Scenario = $scenario; MedianMs = $stats.MedianMs; MinMs = $stats.MinMs; MaxMs = $stats.MaxMs; UnitsCompiled = $units[$scenario] }
    }

    $environment.GetEnumerator() | ForEach-Object { '{0,-22} {1}' -f $_.Key, $_.Value }
    $results | Format-Table -AutoSize | Out-String | Write-Output
    if ($OutputJson) {
        [ordered]@{ Environment = $environment; Results = $results } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputJson
    }
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
