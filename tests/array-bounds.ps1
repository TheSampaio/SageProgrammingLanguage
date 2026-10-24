param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-bounds-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$cases = @(
    @{ Name = 'static_negative'; Code = 'func main(): none { var a: i32[2]; var x: i32 = a[-1]; }'; Error = 'S185' },
    @{ Name = 'static_end'; Code = 'func main(): none { var a: i32[2]; var x: i32 = a[2]; }'; Error = 'S185' },
    @{ Name = 'static_huge'; Code = 'func main(): none { var a: i32[2]; var x: i32 = a[999999999999999999999999999999]; }'; Error = 'S185' },
    @{ Name = 'static_write'; Code = 'func main(): none { var a: i32[2]; a[2] = 1; }'; Error = 'S185' },
    @{ Name = 'static_increment'; Code = 'func main(): none { var a: i32[2]; a[-1]++; }'; Error = 'S185' },
    @{ Name = 'field_bound'; Code = 'struct Item { data: i32[2]; } func main(): none { var a: Item = {}; var x: i32 = a.data[2]; }'; Error = 'S185' },
    @{ Name = 'zero_length'; Code = 'func main(): none { var a: i32[0]; }'; Error = 'S187' },
    @{ Name = 'zero_length_field'; Code = 'struct Item { data: i32[0]; } func main(): none {}'; Error = 'S187' },
    @{ Name = 'zero_length_parameter'; Code = 'func read(a: i32[0]): none {} func main(): none {}'; Error = 'S187' },
    @{ Name = 'oversized_length'; Code = 'func main(): none { var a: i32[2147483648]; }'; Error = 'S187' },
    @{ Name = 'zero_native_parameter'; Code = 'extern native("stdlib.h") { func fake(a: i32[0]): none; } func main(): none {}'; Error = 'S187' },
    @{ Name = 'dynamic_negative'; Code = 'func main(): none { var a: i32[2]; var i: i32 = -1; var x: i32 = a[i]; }'; Runtime = $true },
    @{ Name = 'dynamic_end'; Code = 'func main(): none { var a: i32[2]; var i: i32 = 2; a[i] = 1; }'; Runtime = $true },
    @{ Name = 'dynamic_unsigned'; Code = 'func main(): none { var a: i32[2]; var i: u64 = 2; var x: i32 = a[i]; }'; Runtime = $true },
    @{ Name = 'dynamic_arithmetic'; Code = 'func main(): none { var a: i32[2]; var i: i32 = 1; var x: i32 = a[i + 1]; }'; Runtime = $true },
    @{ Name = 'parameter_runtime'; Code = 'func read(a: i32[2], i: i32): i32 { return a[i]; } func main(): none { var a: i32[2]; var x: i32 = read(a, 2); }'; Runtime = $true },
    @{ Name = 'static_valid'; Code = 'func main(): none { var a: i32[2]; a[0] = 1; a[1]++; }' },
    @{ Name = 'dynamic_valid'; Code = 'func main(): none { var a: i32[2]; var i: i32 = 1; a[i] = 1; }' },
    @{ Name = 'unsafe_array_still_checked'; Code = 'func main(): none { unsafe { var a: i32[2]; var i: i32 = 2; var x: i32 = a[i]; } }'; Runtime = $true }
)

try {
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
        } elseif ($case.Runtime) {
            if ($exitCode -ne 1 -or $text -notmatch 'runtime error S186:' -or
                !(Test-Path -LiteralPath (Join-Path $probeDir "bin/$($case.Name).exe"))) {
                throw "$($case.Name): expected native bounds failure S186 with exit 1, got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful execution.`n$text"
        }
    }
    Write-Output "Array bounds checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
