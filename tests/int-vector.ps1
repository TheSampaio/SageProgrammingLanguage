param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-int-vector-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'use_after_move'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); var b: IntVector = a; a.length(); }'; Error = 'S140' },
    @{ Name = 'use_after_release'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); int_vector::release(a); a.push(1); }'; Error = 'S140' },
    @{ Name = 'shared_push'; Code = 'func add(values: &IntVector): none { values.push(1); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'shared_pop'; Code = 'func take(values: &IntVector): none { values.pop(); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'call_beside_borrow'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); a.push(a.length() as i32); }'; Error = 'S177' },
    @{ Name = 'consuming_method_rejected'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); a.release(); }'; Error = 'S159' },
    @{ Name = 'release_live_loan'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); var view: &IntVector = &a; int_vector::release(a); }'; Error = 'S178' },
    @{ Name = 'anonymous_release'; Code = 'func main(): none { int_vector::release(int_vector::create(4)); }'; Error = 'S194' },
    @{ Name = 'native_requires_unsafe'; Code = 'func main(): none { int_vector_native::sage_int_vector_create(1); }'; Error = 'S164' },
    @{ Name = 'private_storage'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); a.data; }'; Error = 'S109' },
    @{ Name = 'wide_index_requires_conversion'; Code = 'func main(): none { var a: IntVector = int_vector::create(4); var i: u64 = 0; a.get(i); }'; Error = 'S161' }
)
try {
    foreach ($name in @('int_vector.sg', 'test.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $headerPath = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_int_vector.h').Replace('\', '/')

    # Native contract: deferred storage, growth policy, allocation failure, limits, cleanup.
    $nativeSource = Join-Path $probeDir 'native.c'
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/int-vector-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $fixture.Replace('@VECTOR_HEADER@', $headerPath)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native vector fixture build failed.`n$($output -join "`n")" }
    $output = & $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native vector checks failed.`n$($output -join "`n")" }

    $source = Join-Path $sourceDir 'vector_checks.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/tests/int_vector.test.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $passes -ne 26) { throw "Vector integration failed ($passes/26).`n$($output -join "`n")" }

    # Exactly-once cleanup of owners and (re)allocated element storage.
    $logPath = (Join-Path $probeDir 'allocations.log').Replace('\', '/')
    $trackerPath = (Join-Path $probeDir 'vector-tracker.h').Replace('\', '/')
    $tracker = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/allocation-tracker.h') -Raw
    Set-Content -LiteralPath $trackerPath -Value $tracker.Replace('@LOG_PATH@', $logPath).Replace('@HEADER@', $headerPath)
    $modulePath = Join-Path $stdDir 'int_vector.sg'
    $module = Get-Content -LiteralPath $modulePath -Raw
    Set-Content -LiteralPath $modulePath -Value $module.Replace('sage_int_vector.h', $trackerPath)
    $cleanupSource = Join-Path $sourceDir 'cleanup.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/int-vector-cleanup.sg') -Destination $cleanupSource
    $output = & dotnet $compilerPath $cleanupSource 2>&1
    $cleanupPasses = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $cleanupPasses -ne 4) { throw "Vector cleanup integration failed.`n$($output -join "`n")" }
    $active = @{}
    $allocations = 0; $releases = 0
    foreach ($line in Get-Content -LiteralPath $logPath) {
        $event, $pointer = $line -split ' ', 2
        if ($event -eq 'allocate') {
            if ($active.ContainsKey($pointer)) { throw 'Allocation reused a still-live vector address.' }
            $active[$pointer] = $true; $allocations++
        } elseif ($event -eq 'free') {
            if (!$active.ContainsKey($pointer)) { throw 'Unknown or duplicate vector release.' }
            $active.Remove($pointer); $releases++
        } else { throw "Unknown tracker event: $line" }
    }
    if ($active.Count -ne 0 -or $allocations -ne 22 -or $releases -ne 22) { throw "Vector cleanup mismatch: $allocations allocations, $releases releases, $($active.Count) live." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/int_vector.sg') -Destination $modulePath

    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use int_vector; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected semantic $($case.Error).`n$($output -join "`n")" }
    }
    if (@(Get-ChildItem -LiteralPath $stdDir -Recurse -Force -File | Where-Object Extension -ne '.sg').Count -ne 0) { throw 'Copied std must contain only Sage files.' }
    Write-Output "Int vector checks passed: native contract, $passes Sage assertions, $allocations allocations / $releases frees, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-int-vector-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
