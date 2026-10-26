param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$fixtures = Join-Path $PSScriptRoot 'fixtures'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-cpp-isolation-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
$shimDir = Join-Path $probeDir 'shim'
$marker = Join-Path $probeDir 'gpp-invoked.txt'
New-Item -ItemType Directory -Path $sourceDir, $stdDir, $shimDir | Out-Null
$originalPath = $env:PATH

function Invoke-Sage([string]$Name, [string]$Code) {
    $source = Join-Path $sourceDir ($Name + '.sg')
    Set-Content -LiteralPath $source -Value $Code
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & dotnet $compilerPath $source 2>&1 }
    finally { $ErrorActionPreference = $previousErrorAction }
    return @{ Status = $LASTEXITCODE; Text = ($output -join "`n") }
}

try {
    # A fake g++ that records each invocation and fails. Any build that reaches it proves C++ was requested.
    $shimSource = Join-Path $shimDir 'shim.c'
    Set-Content -LiteralPath $shimSource -Value ('#include <stdio.h>' + "`n#include <stdlib.h>`n" + 'int main(void) { FILE* f = fopen(getenv("SAGE_GPP_MARKER"), "a"); if (f) { fputs("g++\n", f); fclose(f); } return 1; }')
    & gcc $shimSource -o (Join-Path $shimDir 'g++.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the g++ shim.' }
    $env:SAGE_GPP_MARKER = $marker
    $env:PATH = $shimDir + [IO.Path]::PathSeparator + $originalPath

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/console.sg') -Destination $stdDir
    Copy-Item -LiteralPath (Join-Path $fixtures 'cpp-interop.hpp') -Destination $sourceDir
    Set-Content -LiteralPath (Join-Path $sourceDir 'plain.h') -Value 'int plain_double(int value);'
    Set-Content -LiteralPath (Join-Path $sourceDir 'plain.c') -Value 'int plain_double(int value) { return value * 2; }'

    # C-only programs: std modules, C externs (including one named 'cpp'), and a declared C source.
    $programs = @(
        @{ Name = 'plain_console'; Code = 'use console; func main(): none { console::print_line("[PASS] plain"); }'; Expect = '[PASS] plain' },
        @{ Name = 'plain_alias'; Code = 'extern cpp("stdlib.h") { func abs(value: c_int): c_int; } func main(): none { unsafe { cpp::abs(-2); } }'; Expect = '' },
        @{ Name = 'plain_source'; Code = 'use console; extern plain("plain.h") { source "plain.c"; func plain_double(value: c_int): c_int; } func main(): none { unsafe { var x: i32 = plain::plain_double(21) as i32; console::print_line("x={x}"); } }'; Expect = 'x=42' }
    )
    foreach ($program in $programs) {
        $result = Invoke-Sage $program.Name $program.Code
        if ($result.Status -ne 0 -or !$result.Text.Contains($program.Expect)) { throw "$($program.Name) must build without a C++ toolchain.`n$($result.Text)" }
    }
    if (Test-Path -LiteralPath $marker) { throw 'A project without C++ must never invoke g++.' }

    # Positive control: the same shim is reached as soon as an extern cpp block exists.
    $cpp = Invoke-Sage 'uses_cpp' 'extern cpp geo("cpp-interop.hpp") { func add(a: c_int, b: c_int): c_int = demo::add; } func main(): none { unsafe { geo::add(1, 2); } }'
    if ($cpp.Status -eq 0 -or !(Test-Path -LiteralPath $marker)) { throw "An extern cpp project must invoke g++ (shim control).`n$($cpp.Text)" }
    Write-Output 'C++ toolchain isolation checks passed: 3 C-only programs never invoked g++, 1 control did.'
}
finally {
    $env:PATH = $originalPath
    Remove-Item Env:\SAGE_GPP_MARKER -ErrorAction SilentlyContinue
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-cpp-isolation-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside isolation test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
