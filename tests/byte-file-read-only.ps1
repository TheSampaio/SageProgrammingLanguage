param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-read-only-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
$binDir = Join-Path $projectDir 'bin'
$inputPath = Join-Path $binDir 'existing.bin'
New-Item -ItemType Directory -Path $sourceDir, $stdDir, $binDir | Out-Null
$cases = @(
    @{ Name = 'wrong_path_capacity'; Code = 'func main(): none { var path: u8[2]; var length: u16 = 1; var file: ByteFile = byte_file::open_read_bytes(path, length); }'; Error = 'S161' },
    @{ Name = 'fabricated_path_array'; Code = 'func main(): none { var length: u16 = 1; var file: ByteFile = byte_file::open_read_bytes(0 as u8[260], length); }'; Error = 'S188' },
    @{ Name = 'wide_path_length'; Code = 'func main(): none { var path: u8[260]; var length: u32 = 1; var file: ByteFile = byte_file::open_read_bytes(path, length); }'; Error = 'S161' },
    @{ Name = 'direct_native_requires_unsafe'; Code = 'func main(): none { byte_file_read_only_native::sage_byte_file_open_read_bytes(0, 1); }'; Error = 'S164' },
    @{ Name = 'query_after_close'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); byte_file::close(file); byte_file::can_write(&file); }'; Error = 'S140' }
)
try {
    foreach ($name in @('byte_file.sg', 'byte_buffer.sg', 'text.sg', 'test.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $includeDir = Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include'
    $baseHeader = (Join-Path $includeDir 'sage_byte_file.h').Replace('\', '/')
    $readHeader = (Join-Path $includeDir 'sage_byte_file_read_only.h').Replace('\', '/')
    $blockHeader = (Join-Path $includeDir 'sage_byte_file_buffer.h').Replace('\', '/')
    $nativeSource = Join-Path $probeDir 'native.c'
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-read-only-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $fixture.Replace('@READ_HEADER@', $readHeader).Replace('@BLOCK_HEADER@', $blockHeader)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native read-only fixture build failed.`n$($output -join "`n")" }
    $nativePath = Join-Path $probeDir 'native.bin'
    $missingNativePath = Join-Path $probeDir 'missing.bin'
    $output = & $nativeExe $nativePath $missingNativePath 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]') { throw "Native read-only checks failed.`n$($output -join "`n")" }
    Write-Output ($output -join "`n")
    if (Test-Path -LiteralPath $missingNativePath) { throw 'Read mode created a missing native file' }
    $bytes = [IO.File]::ReadAllBytes($nativePath)
    if ($bytes.Length -ne 256) { throw 'Native read mode changed file length' }
    for ($index = 0; $index -lt 256; $index++) { if ($bytes[$index] -ne $index) { throw "Native read mode changed byte $index" } }

    # Instrument actual resource opens/closes across all generated translation
    # units, including the caller's scope/early-return cleanup.
    $logPath = (Join-Path $probeDir 'streams.log').Replace('\', '/')
    $trackerPath = (Join-Path $probeDir 'read-tracker.h').Replace('\', '/')
    $tracker = @'
#ifndef SAGE_READ_ONLY_TEST_TRACKER_H
#define SAGE_READ_ONLY_TEST_TRACKER_H
#include <stdio.h>
#include <stdlib.h>
static void record_stream_event(const char* event, void* pointer)
{
    if (!pointer) return;
    FILE* log = fopen("@LOG_PATH@", "a");
    if (!log) exit(90);
    fprintf(log, "%s %p\n", event, pointer);
    if (fclose(log) != 0) exit(91);
}
static FILE* record_stream_open(const char* path, const char* mode)
{
    FILE* stream = fopen(path, mode);
    record_stream_event("open", stream);
    return stream;
}
static int record_stream_close(FILE* stream)
{
    record_stream_event("close", stream);
    return fclose(stream);
}
#define fopen record_stream_open
#define fclose record_stream_close
#include "@BASE_HEADER@"
#include "@READ_HEADER@"
#include "@BLOCK_HEADER@"
#undef fopen
#undef fclose
#endif
'@
    Set-Content -LiteralPath $trackerPath -Value $tracker.Replace('@LOG_PATH@', $logPath).Replace('@BASE_HEADER@', $baseHeader).Replace('@READ_HEADER@', $readHeader).Replace('@BLOCK_HEADER@', $blockHeader)
    $modulePath = Join-Path $stdDir 'byte_file.sg'
    $module = Get-Content -LiteralPath $modulePath -Raw
    foreach ($name in @('sage_byte_file.h', 'sage_byte_file_read_only.h', 'sage_byte_file_buffer.h')) { $module = $module.Replace($name, $trackerPath) }
    Set-Content -LiteralPath $modulePath -Value $module
    [IO.File]::WriteAllBytes($inputPath, [byte[]](0..255))
    (Get-Item -LiteralPath $inputPath).IsReadOnly = $true
    $source = Join-Path $sourceDir 'read_only.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-read-only.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $passes -ne 19) { throw "Read-only Sage integration failed ($passes/19).`n$($output -join "`n")" }
    if (Test-Path -LiteralPath (Join-Path $binDir 'missing.bin')) { throw 'Read mode created a missing Sage file' }
    if (!(Get-Item -LiteralPath $inputPath).IsReadOnly) { throw 'Source read-only attribute changed' }
    $bytes = [IO.File]::ReadAllBytes($inputPath)
    if ($bytes.Length -ne 256) { throw 'Read-only Sage file length changed' }
    for ($index = 0; $index -lt 256; $index++) { if ($bytes[$index] -ne $index) { throw "Read-only Sage byte changed at $index" } }
    $active = @{}; $opens = 0; $closes = 0
    foreach ($line in Get-Content -LiteralPath $logPath) {
        $event, $pointer = $line -split ' ', 2
        if ($event -eq 'open') {
            if ($active.ContainsKey($pointer)) { throw 'Open reused a live FILE pointer' }
            $active[$pointer] = $true; $opens++
        } elseif ($event -eq 'close') {
            if (!$active.ContainsKey($pointer)) { throw 'Unknown or duplicate file close' }
            $active.Remove($pointer); $closes++
        } else { throw "Unknown stream event: $line" }
    }
    if ($active.Count -ne 0 -or $opens -ne 4 -or $closes -ne 4) { throw "Stream cleanup mismatch: $opens opens, $closes closes, $($active.Count) live" }
    $stream = [IO.File]::Open($inputPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $stream.Dispose()
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/byte_file.sg') -Destination $modulePath
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use byte_file; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected semantic $($case.Error).`n$($output -join "`n")" }
    }
    if (@(Get-ChildItem -LiteralPath $stdDir -Recurse -Force -File | Where-Object Extension -ne '.sg').Count -ne 0) { throw 'Copied std must contain only Sage files' }
    Write-Output "Read-only checks passed: $passes Sage assertions, unchanged read-only source, 4 tracked opens/closes, exclusive reopen, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-read-only-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    if (Test-Path -LiteralPath $inputPath) { (Get-Item -LiteralPath $inputPath).IsReadOnly = $false }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
