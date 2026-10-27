param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-byte-file-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'create_requires_unsafe'; Code = 'func main(): none { var value: ByteFile = byte_file::create("not-run.bin"); }'; Error = 'S164' },
    @{ Name = 'byte_path_requires_exact_capacity'; Code = 'func main(): none { var path: u8[2]; var length: u16 = 1; var value: ByteFile = byte_file::create_bytes(path, length); }'; Error = 'S161' },
    @{ Name = 'byte_path_cannot_be_fabricated'; Code = 'func main(): none { var length: u16 = 1; var value: ByteFile = byte_file::create_bytes(0 as u8[260], length); }'; Error = 'S188' },
    @{ Name = 'write_requires_explicit_borrow'; Code = 'func main(): none { var value: ByteFile = byte_file::empty(); var byte: u8 = 1; byte_file::write_byte(value, byte); }'; Error = 'S170' },
    @{ Name = 'text_path_requires_borrow'; Code = 'use text; func main(): none { var path: Text = text::from("a.bin"); var value: ByteFile = byte_file::create_text(path); }'; Error = 'S170' },
    @{ Name = 'read_text_shared_target'; Code = 'use text; func load(file: &mut ByteFile, target: &Text): none { byte_file::read_text(&mut file, &mut target); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'shared_write_rejected'; Code = 'func mutate(value: &ByteFile): none { var byte: u8 = 1; byte_file::write_byte(&mut value, byte); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'close_cannot_consume_borrow'; Code = 'func close_borrow(value: &mut ByteFile): b8 { return byte_file::close(value); } func main(): none {}'; Error = 'S191' },
    @{ Name = 'close_invalidates_owner'; Code = 'func main(): none { var value: ByteFile = byte_file::empty(); var closed: b8 = byte_file::close(value); byte_file::is_open(&value); }'; Error = 'S140' },
    @{ Name = 'double_close_rejected'; Code = 'func main(): none { var value: ByteFile = byte_file::empty(); byte_file::close(value); byte_file::close(value); }'; Error = 'S140' },
    @{ Name = 'close_with_live_loan'; Code = 'func main(): none { var value: ByteFile = byte_file::empty(); var view: &ByteFile = &value; byte_file::close(value); }'; Error = 'S178' },
    @{ Name = 'anonymous_close_rejected'; Code = 'func main(): none { byte_file::close(byte_file::empty()); }'; Error = 'S194' },
    @{ Name = 'native_backend_requires_unsafe'; Code = 'func main(): none { byte_file_native::sage_byte_file_create("not-run.bin"); }'; Error = 'S164' },
    @{ Name = 'wide_byte_not_implicitly_narrowed'; Code = 'func main(): none { var value: ByteFile = byte_file::empty(); var byte: i32 = 300; byte_file::write_byte(&mut value, byte); }'; Error = 'S161' }
)
try {
    foreach ($name in @('byte_file.sg', 'byte_buffer.sg', 'text.sg', 'test.sg', 'console.sg', 'file.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }
    $headerPath = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_byte_file.h').Replace('\', '/')
    $nativeSource = Join-Path $probeDir 'native.c'
    $nativeFixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-native.c') -Raw
    Set-Content -LiteralPath $nativeSource -Value $nativeFixture.Replace('@BYTE_HEADER@', $headerPath)
    $nativeExe = Join-Path $probeDir 'native.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror $nativeSource -o $nativeExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native adapter test build failed.`n$($output -join "`n")" }
    $output = & $nativeExe (Join-Path $probeDir 'native.bin') 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match '\[FAIL\]') { throw "Native adapter checks failed.`n$($output -join "`n")" }
    Write-Output ($output -join "`n")

    $source = Join-Path $sourceDir 'roundtrip.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/byte-file-integration.sg') -Destination $source
    $output = & dotnet $compilerPath $source 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($exitCode -ne 0 -or $text -match '\[FAIL\]|error |Fatal' -or $passes -ne 23) {
        throw "Byte file Sage integration failed ($passes/23 PASS).`n$text"
    }
    $roundtripPath = Join-Path $projectDir 'bin/roundtrip.bin'
    $bytes = [IO.File]::ReadAllBytes($roundtripPath)
    if ($bytes.Length -ne 256) { throw 'Expected 256 persisted bytes.' }
    for ($index = 0; $index -lt 256; $index++) {
        $expected = if ($index -eq 1) { 9 } else { $index }
        if ($bytes[$index] -ne $expected) { throw "Persisted byte mismatch at $index" }
    }
    $automaticPath = Join-Path $projectDir 'bin/automatic.bin'
    if (([IO.File]::ReadAllBytes($automaticPath) -join ',') -ne '11') { throw 'Early-return output was not closed/flushed.' }
    $safePath = Join-Path $projectDir 'bin/safe.bin'
    if (([IO.File]::ReadAllBytes($safePath) -join ',') -ne '42') { throw 'Safe byte-path creation failed or invalid prefix truncated the file.' }
    foreach ($path in @($roundtripPath, $automaticPath, $safePath)) {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $stream.Dispose()
    }
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use byte_file; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        $text = $output -join "`n"
        if ($LASTEXITCODE -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) {
            throw "$($case.Name): expected semantic $($case.Error) before C generation.`n$text"
        }
    }
    Write-Output "Byte file integration checks passed: $passes assertions, persisted bytes/exclusive reopen, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-byte-file-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside byte-file test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
