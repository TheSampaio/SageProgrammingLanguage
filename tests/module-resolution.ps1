param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-modules-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

$cases = @(
    @{ Name = 'qualified_and_internal'; Code = 'use alpha; use beta; extern io("stdio.h") { func puts(text: str): c_int; } unsafe func main(): none { if (alpha::indirect() == 11 && beta::indirect() == 22) { io::puts("[PASS] module_origin"); } else { io::puts("[FAIL] module_origin"); } }'; Pass = $true },
    @{ Name = 'explicit_import_qualification'; Code = 'use alpha; use beta; func main(): none { var x: i32 = value(); }'; Error = 'S105' },
    @{ Name = 'duplicate_global'; Code = 'func value(): i32 { return 1; } func value(): i32 { return 2; } func main(): none {}'; Error = 'S150' },
    @{ Name = 'duplicate_module'; Code = 'module sample { func value(): i32 { return 1; } func value(): i32 { return 2; } } func main(): none {}'; Error = 'S150' },
    @{ Name = 'not_callable'; Code = 'func main(): none { var value: i32 = 1; value(); }'; Error = 'S151' },
    @{ Name = 'local_shadows_module_function'; Code = 'module sample { func value(): i32 { return 1; } func invoke(): i32 { var value: i32 = 3; return value(); } } func main(): none {}'; Error = 'S151' },
    @{ Name = 'global_and_module'; Code = 'use alpha; func value(): i32 { return 33; } extern io("stdio.h") { func puts(text: str): c_int; } unsafe func main(): none { if (alpha::indirect() == 11 && value() == 33) { io::puts("[PASS] module_origin"); } else { io::puts("[FAIL] module_origin"); } }'; Pass = $true },
    @{ Name = 'forward_global'; Code = 'extern io("stdio.h") { func puts(text: str): c_int; } unsafe func main(): none { if (later() == 5) { io::puts("[PASS] module_origin"); } else { io::puts("[FAIL] module_origin"); } } func later(): i32 { return 5; }'; Pass = $true },
    @{ Name = 'native_alias_collision'; Code = 'extern sample("stdio.h") { func value(): c_int; } module sample { func value(): i32 { return 1; } } func main(): none {}'; Error = 'S150' }
)

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'alpha.sg') -Value 'module alpha { func indirect(): i32 { return value(); } func value(): i32 { return 11; } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'beta.sg') -Value 'module beta { func indirect(): i32 { return value(); } func value(): i32 { return 22; } }'
    $source = Join-Path $sourceDir 'main.sg'
    foreach ($case in $cases) {
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath --verbose $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -eq 0 -or $text -notmatch ("error " + $case.Error + ':')) {
                throw "$($case.Name): expected $($case.Error).`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -notmatch '\[PASS\] module_origin' -or $text -match '\[FAIL\]') {
            throw "$($case.Name): failed to resolve the expected callable origin.`n$text"
        }
        if ($case.Name -eq 'qualified_and_internal') {
            $ast = Get-Content -LiteralPath (Join-Path $probeDir 'obj/alpha.ast') -Raw
            if ($ast -notmatch '"ResolvedName":\s*"alpha::value"') {
                throw 'The analyzed AST did not preserve the callable origin.'
            }
        }
    }
    Write-Output "Module resolution checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
