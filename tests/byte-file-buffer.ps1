param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-byte-block-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'read_shared_file'; Code = 'func bad(file: &ByteFile, buffer: &mut ByteBuffer): none { byte_file::read_buffer(&mut file, &mut buffer, 1); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'read_shared_target'; Code = 'func bad(file: &mut ByteFile, buffer: &ByteBuffer): none { byte_file::read_buffer(&mut file, &mut buffer, 1); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'write_shared_file'; Code = 'func bad(file: &ByteFile, buffer: &ByteBuffer): none { byte_file::write_buffer(&mut file, &buffer, 1); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'missing_target_borrow'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); var buffer: ByteBuffer = byte_buffer::empty(); byte_file::read_buffer(&mut file, buffer, 1); }'; Error = 'S170' },
    @{ Name = 'missing_source_borrow'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); var buffer: ByteBuffer = byte_buffer::empty(); byte_file::write_buffer(&mut file, buffer, 1); }'; Error = 'S170' },
    @{ Name = 'closed_file_use'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); var buffer: ByteBuffer = byte_buffer::empty(); byte_file::close(file); byte_file::read_buffer(&mut file, &mut buffer, 1); }'; Error = 'S140' },
    @{ Name = 'released_buffer_use'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); var buffer: ByteBuffer = byte_buffer::empty(); byte_buffer::release(buffer); byte_file::write_buffer(&mut file, &buffer, 1); }'; Error = 'S140' },
    @{ Name = 'wide_limit'; Code = 'func main(): none { var file: ByteFile = byte_file::empty(); var buffer: ByteBuffer = byte_buffer::empty(); var limit: u64 = 1; byte_file::read_buffer(&mut file, &mut buffer, limit); }'; Error = 'S161' },
    @{ Name = 'direct_native_requires_unsafe'; Code = 'func main(): none { byte_file_buffer_native::sage_byte_file_read_buffer(0, 0, 1); }'; Error = 'S164' }
)
try {
    foreach ($name in @('byte_file.sg', 'byte_buffer.sg', 'test.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $header = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_byte_file_buffer.h').Replace('\', '/')
    $nativeSource = Join-Path $probeDir 'native.c'
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-buffer-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $fixture.Replace('@BLOCK_HEADER@', $header)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native block fixture build failed.`n$($output -join "`n")" }
    $nativePath = Join-Path $probeDir 'native.bin'
    $output = & $nativeExe $nativePath 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]') { throw "Native block checks failed.`n$($output -join "`n")" }
    Write-Output ($output -join "`n")
    $bytes = [IO.File]::ReadAllBytes($nativePath)
    if ($bytes.Length -ne 256) { throw 'Unexpected native block output length' }
    for ($index = 0; $index -lt 256; $index++) {
        $expected = if ($index -lt 3) { 99 - $index } else { $index }
        if ($bytes[$index] -ne $expected) { throw "Unexpected native block byte at $index" }
    }
    $source = Join-Path $sourceDir 'blocks.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-buffer.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]|error |Fatal' -or $passes -ne 21) { throw "Block Sage integration failed ($passes/21).`n$($output -join "`n")" }
    $filePath = Join-Path $projectDir 'bin/block.bin'
    $bytes = [IO.File]::ReadAllBytes($filePath)
    if ($bytes.Length -ne 256) { throw 'Unexpected Sage block output length' }
    for ($index = 0; $index -lt 256; $index++) { if ($bytes[$index] -ne $index) { throw "Unexpected Sage block byte at $index" } }
    $stream = [IO.File]::Open($filePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $stream.Dispose()
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use byte_file; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected semantic $($case.Error).`n$($output -join "`n")" }
    }
    if (@(Get-ChildItem -LiteralPath $stdDir -Recurse -Force -File | Where-Object Extension -ne '.sg').Count -ne 0) { throw 'Copied std must contain only Sage files' }
    Write-Output "Block I/O checks passed: $passes Sage assertions, persisted native/Sage bytes, exclusive reopen, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-byte-block-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
