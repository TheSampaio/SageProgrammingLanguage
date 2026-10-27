param([Parameter(Mandatory = $true)][string]$CompilerDll)

# Builds and runs the Sandbox application and examples against a copy of the
# shipped std, and checks that ordinary app code needs no unsafe context.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$sandbox = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../Sandbox')).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-sandbox-apps-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null

function New-App([string]$name, [string]$source) {
    $dir = Join-Path $probeDir $name
    New-Item -ItemType Directory -Path (Join-Path $dir 'src'), (Join-Path $dir 'std') | Out-Null
    Copy-Item -Path (Join-Path $sandbox 'std/*.sg') -Destination (Join-Path $dir 'std')
    Copy-Item -LiteralPath $source -Destination (Join-Path $dir 'src/main.sg')
    Push-Location $dir
    try { $output = & dotnet $compilerPath build 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "$name build failed.`n$($output -join "`n")" }
    return Join-Path $dir "bin/$name.exe"
}

try {
    $safeSources = @((Join-Path $sandbox 'src/main.sg')) + @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'examples') -Filter *.sg |
        Where-Object Name -ne 'native_resource_contract.sg' | ForEach-Object FullName)
    foreach ($source in $safeSources) {
        if ((Get-Content -LiteralPath $source -Raw) -match '\bunsafe\s*(\{|func\b)') { throw "Application code should not need unsafe: $source" }
    }

    # Savings calculator: output plus the CSV report written through byte_file/Text.
    $app = New-App 'savings' (Join-Path $sandbox 'src/main.sg')
    $runDir = Split-Path -Parent $app
    Push-Location $runDir
    try { $output = (& $app) -join "`n"; $status = $LASTEXITCODE } finally { Pop-Location }
    if ($status -ne 0 -or $output -notmatch 'Final balance:  \$1967\.15' -or $output -notmatch 'Best year gain: 12870 cents' -or $output -notmatch 'Report saved to savings_report\.csv') { throw "Unexpected savings output.`n$output" }
    $report = @(Get-Content -LiteralPath (Join-Path $runDir 'savings_report.csv'))
    if ($report.Count -ne 11 -or $report[0] -ne 'year,balance' -or $report[10] -ne '10,1967.15') { throw "Unexpected report.`n$($report -join "`n")" }

    # Bounded console input demo fed through a pipe.
    $greeting = New-App 'greeting' (Join-Path $sandbox 'examples/greeting.sg')
    $inputPath = Join-Path $probeDir 'name.txt'
    [IO.File]::WriteAllBytes($inputPath, [Text.Encoding]::ASCII.GetBytes("Ada`r`n"))
    $output = (& cmd /c "`"$greeting`" < `"$inputPath`"") -join "`n"
    if ($LASTEXITCODE -ne 0 -or $output -notmatch 'Hello, Ada!$') { throw "Unexpected greeting output.`n$output" }

    # Long-running examples are only built.
    $null = New-App 'echo' (Join-Path $sandbox 'examples/http_echo.sg')
    $buffer = New-App 'buffer' (Join-Path $sandbox 'examples/owned_byte_buffer.sg')
    $output = (& $buffer) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $output -notmatch 'Buffer value: 42') { throw "Unexpected buffer example output.`n$output" }
    Write-Output "Sandbox apps passed: savings app and CSV report, piped greeting, echo server build, byte buffer example, $($safeSources.Count) sources without unsafe."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-sandbox-apps-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
