param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

# Ordinary compilers stay quiet and write no report files. When the compiler sources are next to
# the DLL (bin/<Configuration>/net8.0), the Instrumented configuration is built and its report checked.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-metrics-probe-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Invoke-Build([string]$Compiler, [string[]]$Extra = @()) {
    Push-Location $probeDir
    # The instrumentation report goes to stderr, which Windows PowerShell would otherwise turn into an error.
    $ErrorActionPreference = 'Continue'
    try { $output = & dotnet $Compiler build @Extra 2>&1 | ForEach-Object { "$_" } }
    finally { Pop-Location }
    $text = $output -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Build failed.`n$text" }
    return $text
}

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'main.sg') -Value 'use alpha; func main(): none { var x: i32 = alpha::value(); }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'alpha.sg') -Value 'module alpha { func value(): i32 { return 7; } }'

    $quiet = Invoke-Build $compilerPath
    if ($quiet -match '\[sage-instrumentation\]') { throw "Ordinary builds must not report instrumentation.`n$quiet" }
    $allowed = '^(alpha|main)\.(c|h|o)$|^sage\.h$|^sage-build-cache\.json$|^\.sage-build\.lock$'
    $unexpected = Get-ChildItem -LiteralPath (Join-Path $probeDir 'obj') -Force | Where-Object { $_.Name -notmatch $allowed }
    if ($unexpected) { throw "Unexpected build artifacts: $($unexpected.Name -join ', ')" }
    $binFiles = @(Get-ChildItem -LiteralPath (Join-Path $probeDir 'bin') -Force)
    if ($binFiles.Count -ne 1 -or $binFiles[0].Extension -ne '.exe') { throw "bin/ must contain only the executable." }

    $project = Join-Path (Split-Path $compilerPath) '..\..\..\Sage.csproj'
    if (Test-Path -LiteralPath $project) {
        $build = & dotnet build $project -c Instrumented -nologo -v q 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Instrumented compiler build failed.`n$($build -join "`n")" }
        $instrumented = Join-Path (Split-Path $project) 'bin\Instrumented\net8.0\Sage.dll'

        $clean = Invoke-Build $instrumented @('--clean')
        foreach ($metric in 'lex_ms', 'parse_ms', 'semantic_ms', 'codegen_ms', 'cache_ms', 'native_compile_ms', 'link_ms', 'toolchain_probe_ms', 'process_ms', 'peak_working_set_bytes', 'allocated_bytes') {
            if ($clean -notmatch "\[sage-instrumentation\] $metric=\d") { throw "Missing metric $metric.`n$clean" }
        }
        if ($clean -notmatch '\[sage-instrumentation\] units_compiled=2' -or $clean -notmatch '\[sage-instrumentation\] relinked=True') {
            throw "A clean instrumented build must compile both units and link.`n$clean"
        }
        $incremental = Invoke-Build $instrumented
        if ($incremental -notmatch '\[sage-instrumentation\] units_compiled=0' -or $incremental -notmatch '\[sage-instrumentation\] units_reused=2') {
            throw "A no-change instrumented build must reuse both units.`n$incremental"
        }
        Write-Output 'Instrumentation checks passed (quiet default and Instrumented report).'
    }
    else {
        Write-Output 'Instrumentation checks passed (quiet default; compiler sources not found, report check skipped).'
    }
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
