param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$fixtures = Join-Path $PSScriptRoot 'fixtures/native-sources'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-native-sources-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
$objDir = Join-Path $probeDir 'obj'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null

function Invoke-Sage([string]$Name, [string]$Code) {
    $source = Join-Path $sourceDir ($Name + '.sg')
    if ($Code) { Set-Content -LiteralPath $source -Value $Code }
    $output = & dotnet $compilerPath $source 2>&1
    return @{ Status = $LASTEXITCODE; Text = ($output -join "`n") }
}

function Clear-Objects { if (Test-Path -LiteralPath $objDir) { Remove-Item -LiteralPath $objDir -Recurse -Force } }

$report = 'extern report("report.h") { source "report.c"; func report_value(label: c_char*, value: c_int): none; } '
$negative = @(
    @{ Name = 'missing_source'; Code = 'extern native("stdlib.h") { source "absent.c"; } func main(): none {}' },
    @{ Name = 'header_as_source'; Code = 'extern native("report.h") { source "report.h"; } func main(): none {}' },
    @{ Name = 'source_without_string'; Code = 'extern native("report.h") { source report; } func main(): none {}'; Error = 'S001' }
)

try {
    Copy-Item -Path (Join-Path $fixtures '*') -Destination $sourceDir

    # A C++ library split into header and implementation, plus a C helper, linked into one program.
    $mixed = Invoke-Sage 'main'
    if ($mixed.Status -ne 0) { throw "Declared native sources must build and run.`n$($mixed.Text)" }
    foreach ($expected in 'total=42', 'square=49', '[tally dropped 42]') {
        if (!$mixed.Text.Contains($expected)) { throw "Missing runtime output '$expected'.`n$($mixed.Text)" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $objDir 'sage_native_0_tally.o'))) { throw 'The C++ source must compile to its own object.' }

    # C-only sources stay in the gcc build: no adapters, no C++ objects, no C++ runtime.
    Clear-Objects
    $cOnly = Invoke-Sage 'c_only' ($report + $report + 'func main(): none { unsafe { report::report_value("c" as c_char*, 7); } }')
    if ($cOnly.Status -ne 0 -or !$cOnly.Text.Contains('c=7')) { throw "A repeated C source must link once.`n$($cOnly.Text)" }
    if (Get-ChildItem -LiteralPath $objDir -Filter '*.o') { throw 'A C-only build must not produce C++ objects.' }

    # Sources resolve next to the module that declares them, here an std module.
    # Its header resolves too: source directories join the include path.
    Set-Content -LiteralPath (Join-Path $stdDir 'helper_native.h') -Value 'int native_double(int value);'
    Set-Content -LiteralPath (Join-Path $stdDir 'helper_native.c') -Value 'int native_double(int value) { return value * 2; }'
    Set-Content -LiteralPath (Join-Path $stdDir 'helper.sg') -Value 'module helper { extern helper_native("helper_native.h") { source "helper_native.c"; func native_double(value: c_int): c_int; } func twice(value: i32): i32 { unsafe { return helper_native::native_double(value as c_int) as i32; } } }'
    $imported = Invoke-Sage 'imported' ('use helper; ' + $report + 'func main(): none { unsafe { report::report_value("twice" as c_char*, helper::twice(21) as c_int); } }')
    if ($imported.Status -ne 0 -or !$imported.Text.Contains('twice=42')) { throw "Imported module sources must resolve relative to the module.`n$($imported.Text)" }

    foreach ($case in $negative) {
        Clear-Objects
        $result = Invoke-Sage $case.Name $case.Code
        $code = if ($case.Error) { $case.Error } else { 'S211' }
        if ($result.Status -ne 1 -or $result.Text -notmatch ('error ' + $code + ':') -or
            (Test-Path -LiteralPath (Join-Path $objDir "$($case.Name).c"))) {
            throw "$($case.Name): expected $code before code generation, got status $($result.Status).`n$($result.Text)"
        }
    }
    Write-Output "Native source checks passed: 3 builds, $($negative.Count) rejection cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-native-sources-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside native source test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
