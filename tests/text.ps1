param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-text-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'use_after_move'; Code = 'func main(): none { var a: Text = text::from("a"); var b: Text = a; a.length(); }'; Error = 'S140' },
    @{ Name = 'use_after_release'; Code = 'func main(): none { var a: Text = text::from("a"); text::release(a); a.print(); }'; Error = 'S140' },
    @{ Name = 'use_after_consuming_call'; Code = 'func take(value: Text): none {} func main(): none { var a: Text = text::from("a"); take(a); text::length(&a); }'; Error = 'S140' },
    @{ Name = 'self_append_conflict'; Code = 'func main(): none { var a: Text = text::create(4); a.append(&a); }'; Error = 'S172' },
    @{ Name = 'shared_receiver_mutation'; Code = 'func change(value: &Text): none { value.append_str("x"); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'shared_read_line'; Code = 'func change(value: &Text): none { value.read_line(); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'consuming_method_rejected'; Code = 'func main(): none { var a: Text = text::from("a"); a.release(); }'; Error = 'S159' },
    @{ Name = 'release_live_loan'; Code = 'func main(): none { var a: Text = text::from("a"); var view: &Text = &a; text::release(a); }'; Error = 'S178' },
    @{ Name = 'release_borrowed'; Code = 'func steal(value: &mut Text): none { text::release(value); } func main(): none {}'; Error = 'S191' },
    @{ Name = 'anonymous_release'; Code = 'func main(): none { text::release(text::from("a")); }'; Error = 'S194' },
    @{ Name = 'native_requires_unsafe'; Code = 'func main(): none { text_native::sage_text_create(1); }'; Error = 'S164' },
    @{ Name = 'private_storage'; Code = 'func main(): none { var a: Text = text::from("a"); a.data; }'; Error = 'S109' },
    @{ Name = 'not_a_str'; Code = 'func show(value: str): none {} func main(): none { var a: Text = text::from("a"); show(a); }'; Error = 'S161' }
)
try {
    foreach ($name in @('text.sg', 'test.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $headerPath = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_text.h').Replace('\', '/')

    # Native contract: allocation failure, atomic append, byte order, exact output, line input.
    $nativeSource = Join-Path $probeDir 'native.c'
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/text-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $fixture.Replace('@TEXT_HEADER@', $headerPath)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native text fixture build failed.`n$($output -join "`n")" }
    $output = & $nativeExe (Join-Path $probeDir 'input.txt') (Join-Path $probeDir 'output.txt') 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]') { throw "Native text checks failed.`n$($output -join "`n")" }

    $source = Join-Path $sourceDir 'text_checks.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/tests/text.test.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $passes -ne 40) { throw "Text integration failed ($passes/40).`n$($output -join "`n")" }

    # Bounded line input through a real stdin pipe: build only, then feed exact bytes.
    $lineProject = Join-Path $probeDir 'lines'
    New-Item -ItemType Directory -Path (Join-Path $lineProject 'src'), (Join-Path $lineProject 'std') | Out-Null
    foreach ($name in @('text.sg', 'console.sg')) { Copy-Item -LiteralPath (Join-Path $stdDir $name) -Destination (Join-Path $lineProject 'std') }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/text-line-input.sg') -Destination (Join-Path $lineProject 'src/main.sg')
    Push-Location $lineProject
    try { $output = & dotnet $compilerPath build 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "Line input build failed.`n$($output -join "`n")" }
    # cmd redirection feeds the exact bytes (no encoding preamble or added newline).
    $inputPath = Join-Path $probeDir 'lines.txt'
    [IO.File]::WriteAllBytes($inputPath, [Text.Encoding]::ASCII.GetBytes("hi`r`ntoo long line`n`nabcde`r`ntail"))
    $lineExe = Join-Path $lineProject 'bin/lines.exe'
    $lines = (& cmd /c "`"$lineExe`" < `"$inputPath`"") -join "`n"
    $lineStatus = $LASTEXITCODE
    $expected = @(
        '[hi] count=2 eof=0 truncated=0 failed=0',
        '[too l] count=5 eof=0 truncated=1 failed=0',
        '[] count=0 eof=0 truncated=0 failed=0',
        '[abcde] count=5 eof=0 truncated=0 failed=0',
        '[tail] count=4 eof=1 truncated=0 failed=0',
        'done') -join "`n"
    if ($lineStatus -ne 0 -or $lines.Trim() -ne $expected) { throw "Unexpected line input output:`n$lines" }

    # Exactly-once cleanup across moves, returns, consuming parameters, loops and reassignment.
    $logPath = (Join-Path $probeDir 'allocations.log').Replace('\', '/')
    $trackerPath = (Join-Path $probeDir 'text-tracker.h').Replace('\', '/')
    $tracker = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/allocation-tracker.h') -Raw
    Set-Content -LiteralPath $trackerPath -Value $tracker.Replace('@LOG_PATH@', $logPath).Replace('@HEADER@', $headerPath)
    $modulePath = Join-Path $stdDir 'text.sg'
    $module = Get-Content -LiteralPath $modulePath -Raw
    Set-Content -LiteralPath $modulePath -Value $module.Replace('sage_text.h', $trackerPath)
    $cleanupSource = Join-Path $sourceDir 'cleanup.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/text-cleanup.sg') -Destination $cleanupSource
    $output = & dotnet $compilerPath $cleanupSource 2>&1
    $cleanupPasses = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $cleanupPasses -ne 5) { throw "Text cleanup integration failed.`n$($output -join "`n")" }
    $active = @{}
    $allocations = 0; $releases = 0
    foreach ($line in Get-Content -LiteralPath $logPath) {
        $event, $pointer = $line -split ' ', 2
        if ($event -eq 'allocate') {
            if ($active.ContainsKey($pointer)) { throw 'Allocation reused a still-live text address.' }
            $active[$pointer] = $true; $allocations++
        } elseif ($event -eq 'free') {
            if (!$active.ContainsKey($pointer)) { throw 'Unknown or duplicate text release.' }
            $active.Remove($pointer); $releases++
        } else { throw "Unknown tracker event: $line" }
    }
    # 10 owners from the fixture plus one interpolated report line per assertion (5), all released exactly once.
    if ($active.Count -ne 0 -or $allocations -ne 15 -or $releases -ne 15) { throw "Text cleanup mismatch: $allocations allocations, $releases releases, $($active.Count) live." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/text.sg') -Destination $modulePath

    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use text; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected semantic $($case.Error).`n$($output -join "`n")" }
    }
    if (@(Get-ChildItem -LiteralPath $stdDir -Recurse -Force -File | Where-Object Extension -ne '.sg').Count -ne 0) { throw 'Copied std must contain only Sage files.' }
    Write-Output "Text checks passed: native contract, $passes Sage assertions, stdin line input, $allocations allocations / $releases frees, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-text-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
