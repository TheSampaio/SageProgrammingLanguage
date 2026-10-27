param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-byte-buffer-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'push_requires_borrow'; Code = 'func main(): none { var owner: ByteBuffer = byte_buffer::empty(); var byte: u8 = 1; byte_buffer::push(owner, byte); }'; Error = 'S170' },
    @{ Name = 'shared_mutation'; Code = 'func change(value: &ByteBuffer): none { var byte: u8 = 1; byte_buffer::push(&mut value, byte); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'use_after_release'; Code = 'func main(): none { var owner: ByteBuffer = byte_buffer::empty(); byte_buffer::release(owner); byte_buffer::length(&owner); }'; Error = 'S140' },
    @{ Name = 'use_after_move'; Code = 'func main(): none { var owner: ByteBuffer = byte_buffer::empty(); var moved: ByteBuffer = owner; byte_buffer::read(&owner, 0); }'; Error = 'S140' },
    @{ Name = 'release_borrowed'; Code = 'func steal(value: &mut ByteBuffer): none { byte_buffer::release(value); } func main(): none {}'; Error = 'S191' },
    @{ Name = 'release_live_loan'; Code = 'func main(): none { var owner: ByteBuffer = byte_buffer::empty(); var view: &ByteBuffer = &owner; byte_buffer::release(owner); }'; Error = 'S178' },
    @{ Name = 'anonymous_release'; Code = 'func main(): none { byte_buffer::release(byte_buffer::empty()); }'; Error = 'S194' },
    @{ Name = 'native_requires_unsafe'; Code = 'func main(): none { byte_buffer_native::sage_byte_buffer_create(1); }'; Error = 'S164' },
    @{ Name = 'wide_capacity_requires_conversion'; Code = 'func main(): none { var capacity: u64 = 1; var owner: ByteBuffer = byte_buffer::create(capacity); }'; Error = 'S161' },
    @{ Name = 'private_storage'; Code = 'func main(): none { var owner: ByteBuffer = byte_buffer::empty(); owner.data; }'; Error = 'S109' }
)
try {
    foreach ($name in @('byte_buffer.sg', 'byte_file.sg', 'text.sg', 'test.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $headerPath = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_byte_buffer.h').Replace('\', '/')
    $nativeSource = Join-Path $probeDir 'native.c'
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-buffer-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $fixture.Replace('@BUFFER_HEADER@', $headerPath)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native buffer fixture build failed.`n$($output -join "`n")" }
    $output = & $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]') { throw "Native buffer checks failed.`n$($output -join "`n")" }
    Write-Output ($output -join "`n")

    $source = Join-Path $sourceDir 'buffer.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/tests/byte_buffer.test.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $passes -ne 31) { throw "Buffer integration failed ($passes/31).`n$($output -join "`n")" }

    $fileSource = Join-Path $sourceDir 'buffer_file.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-buffer-file.sg') -Destination $fileSource
    $output = & dotnet $compilerPath $fileSource 2>&1
    $filePasses = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $filePasses -ne 8) { throw "Buffer/file integration failed.`n$($output -join "`n")" }
    $filePath = Join-Path $projectDir 'bin/buffer.bin'
    $bytes = [IO.File]::ReadAllBytes($filePath)
    if ($bytes.Length -ne 256) { throw 'Expected 256 persisted buffer bytes.' }
    for ($index = 0; $index -lt 256; $index++) { if ($bytes[$index] -ne $index) { throw "Buffer/file byte mismatch at $index" } }
    $stream = [IO.File]::Open($filePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $stream.Dispose()

    # Track allocations and frees across generated translation units without adding
    # production hooks or placing native files in the project's std directory.
    $logPath = (Join-Path $probeDir 'allocations.log').Replace('\', '/')
    $trackerPath = (Join-Path $probeDir 'buffer-tracker.h').Replace('\', '/')
    $tracker = @'
#include <stdio.h>
#include <stdlib.h>
static void record_buffer_event(const char* event, void* pointer)
{
    if (!pointer) return;
    FILE* log = fopen("@LOG_PATH@", "a");
    if (!log) exit(90);
    fprintf(log, "%s %p\n", event, pointer);
    if (fclose(log) != 0) exit(91);
}
static void* record_buffer_allocate(size_t size)
{
    void* pointer = malloc(size);
    record_buffer_event("allocate", pointer);
    return pointer;
}
static void record_buffer_free(void* pointer)
{
    record_buffer_event("free", pointer);
    free(pointer);
}
#define malloc record_buffer_allocate
#define free record_buffer_free
#include "@BUFFER_HEADER@"
#undef malloc
#undef free
'@
    Set-Content -LiteralPath $trackerPath -Value $tracker.Replace('@LOG_PATH@', $logPath).Replace('@BUFFER_HEADER@', $headerPath)
    $modulePath = Join-Path $stdDir 'byte_buffer.sg'
    $module = Get-Content -LiteralPath $modulePath -Raw
    Set-Content -LiteralPath $modulePath -Value $module.Replace('sage_byte_buffer.h', $trackerPath)
    $cleanupSource = Join-Path $sourceDir 'cleanup.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-buffer-cleanup.sg') -Destination $cleanupSource
    $output = & dotnet $compilerPath $cleanupSource 2>&1
    $cleanupPasses = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $cleanupPasses -ne 6) { throw "Buffer cleanup integration failed.`n$($output -join "`n")" }
    $active = @{}
    $allocations = 0; $releases = 0
    foreach ($line in Get-Content -LiteralPath $logPath) {
        $event, $pointer = $line -split ' ', 2
        if ($event -eq 'allocate') {
            if ($active.ContainsKey($pointer)) { throw 'Allocation reused a still-live buffer address.' }
            $active[$pointer] = $true; $allocations++
        } elseif ($event -eq 'free') {
            if (!$active.ContainsKey($pointer)) { throw 'Unknown or duplicate buffer release.' }
            $active.Remove($pointer); $releases++
        } else { throw "Unknown tracker event: $line" }
    }
    if ($active.Count -ne 0 -or $allocations -ne 6 -or $releases -ne 6) { throw "Buffer cleanup mismatch: $allocations allocations, $releases releases, $($active.Count) live." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/byte_buffer.sg') -Destination $modulePath
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use byte_buffer; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected semantic $($case.Error).`n$($output -join "`n")" }
    }
    if (@(Get-ChildItem -LiteralPath $stdDir -Recurse -Force -File | Where-Object Extension -ne '.sg').Count -ne 0) { throw 'Copied std must contain only Sage files.' }
    Write-Output "Byte buffer checks passed: $passes Sage assertions, $filePasses file assertions / persisted bytes, $cleanupPasses cleanup assertions / 6 allocations and frees, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-byte-buffer-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
