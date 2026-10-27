param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-visibility-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

$io = 'extern io("stdio.h") { func puts(text: str): c_int; }'
$cases = @(
    @{ Name = 'direct_imports'; Code = "use gamma; use alpha; use helpers; $io unsafe func main(): none { if (gamma::total() == 18 && alpha::value() == 11 && helper() == 7) { io::puts(""[PASS] visibility""); } else { io::puts(""[FAIL] visibility""); } }"; Pass = $true },
    @{ Name = 'transitive_module'; Code = 'use gamma; func main(): none { var x: i32 = alpha::value(); }'; Error = 'S153' },
    @{ Name = 'transitive_top_level'; Code = 'use gamma; func main(): none { var x: i32 = helper(); }'; Error = 'S153' },
    @{ Name = 'transitive_interpolation'; Code = 'use gamma; func main(): none { var text: str = $"{alpha::value()}"; }'; Error = 'S153' },
    @{ Name = 'imported_native_alias'; Code = 'use alpha; unsafe func main(): none { alpha_native::abs(-1); }'; Error = 'S153' },
    @{ Name = 'same_file_module'; Code = "module local { func value(): i32 { return 4; } } $io unsafe func main(): none { if (local::value() == 4) { io::puts(""[PASS] visibility""); } }"; Pass = $true },
    @{ Name = 'flattened_module_names'; Code = 'module a_b { func c(): i32 { return 1; } } module a { func b_c(): i32 { return 2; } } func main(): none {}'; Error = 'S212' },
    @{ Name = 'flattened_top_level'; Code = 'module alpha2 { func value(): i32 { return 1; } } func alpha2_value(): i32 { return 2; } func main(): none {}'; Error = 'S212' },
    @{ Name = 'module_main'; Code = 'module tool { func main(): none {} } func main(): none {}'; Error = 'S212' },
    @{ Name = 'sage_native_symbol'; Code = 'extern io("stdio.h") { func puts(text: str): c_int; } func puts(text: str): c_int { return 0; } func main(): none {}'; Error = 'S212' },
    @{ Name = 'cross_file_flattened'; Code = 'use alpha; func alpha_value(): i32 { return 2; } func main(): none {}'; Error = 'S212' },
    @{ Name = 'shared_native_symbol'; Code = "extern other(""stdio.h"") { func puts(text: str): c_int; } $io unsafe func main(): none { other::puts(""[PASS] visibility""); }"; Pass = $true }
)

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'alpha.sg') -Value 'module alpha { extern alpha_native("stdlib.h") { func abs(value: c_int): c_int; } func value(): i32 { return 11; } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'helpers.sg') -Value 'func helper(): i32 { return 7; }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'gamma.sg') -Value 'use alpha; use helpers; module gamma { func total(): i32 { return alpha::value() + helper(); } }'
    $source = Join-Path $sourceDir 'main.sg'
    foreach ($case in $cases) {
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -eq 0 -or $text -notmatch ("error " + $case.Error + ':')) {
                throw "$($case.Name): expected $($case.Error), got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -notmatch '\[PASS\] visibility' -or $text -match '\[FAIL\]') {
            throw "$($case.Name): expected a successful run.`n$text"
        }
    }
    Write-Output "Module visibility checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
