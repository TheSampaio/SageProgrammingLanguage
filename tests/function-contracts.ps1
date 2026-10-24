param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-contracts-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$cases = @(
    @{ Name = 'too_few'; Code = 'func take(value: i32): none {} func main(): none { take(); }'; Error = 'S160' },
    @{ Name = 'too_many'; Code = 'func take(value: i32): none {} func main(): none { take(1, 2); }'; Error = 'S160' },
    @{ Name = 'wrong_argument'; Code = 'func take(value: i32): none {} func main(): none { take("wrong"); }'; Error = 'S161' },
    @{ Name = 'wrong_return'; Code = 'func make(): i32 { return "wrong"; } func main(): none {}'; Error = 'S162' },
    @{ Name = 'missing_return'; Code = 'func make(): i32 { var x: i32 = 1; } func main(): none {}'; Error = 'S163' },
    @{ Name = 'conditional_return'; Code = 'func make(flag: b8): i32 { if (flag) { return 1; } } func main(): none {}'; Error = 'S163' },
    @{ Name = 'both_returns'; Code = 'func make(flag: b8): i32 { if (flag) { return 1; } else { return 2; } } func main(): none { var x: i32 = make(true); }' },
    @{ Name = 'forward_signature'; Code = 'func main(): none { var x: i32 = later(2); } func later(value: i32): i32 { return value; }' },
    @{ Name = 'literal_promotion'; Code = 'func widen(value: i64): i64 { return value; } func main(): none { var x: i64 = widen(2); }' },
    @{ Name = 'duplicate_parameter'; Code = 'func take(value: i32, value: i32): none {} func main(): none {}'; Error = 'S154' },
    @{ Name = 'unsafe_call'; Code = 'unsafe func native(): none {} func main(): none { native(); }'; Error = 'S164' },
    @{ Name = 'unsafe_context'; Code = 'unsafe func native(): none {} func main(): none { unsafe { native(); } }' },
    @{ Name = 'native_arity'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } func main(): none { c::abs(); }'; Error = 'S160' },
    @{ Name = 'same_native_contract'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } extern c("stdlib.h") { func abs(number: c_int): c_int; } func main(): none { var x: i32 = c::abs(-2) as i32; }' },
    @{ Name = 'native_parameter_conflict'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } extern c("stdlib.h") { func abs(value: c_double): c_int; } func main(): none {}'; Error = 'S152' },
    @{ Name = 'native_return_conflict'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } extern c("stdlib.h") { func abs(value: c_int): c_long; } func main(): none {}'; Error = 'S152' },
    @{ Name = 'native_origin_conflict'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } extern c("math.h") { func abs(value: c_int): c_int; } func main(): none {}'; Error = 'S152' },
    @{ Name = 'native_return_requires_cast'; Code = 'extern c("stdlib.h") { func abs(value: c_int): c_int; } func make(): i32 { return c::abs(-2); } func main(): none {}'; Error = 'S162' },
    @{ Name = 'infinite_loop_no_fallthrough'; Code = 'func never(): i32 { while (true) {} } func main(): none {}' }
)

try {
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -eq 0 -or $text -notmatch ("error " + $case.Error + ':')) {
                throw "$($case.Name): expected $($case.Error), got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful compilation.`n$text"
        }
    }
    Write-Output "Function contract checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
