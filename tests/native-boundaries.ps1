param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-native-boundaries-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$native = 'extern native("stdlib.h") { func abs(value: c_int): c_int; } '
$cases = @(
    @{ Name = 'native_requires_unsafe'; Code = $native + 'func main(): none { var x: i32 = native::abs(-2) as i32; }'; Error = 'S164' },
    @{ Name = 'native_unsafe_block'; Code = $native + 'func main(): none { unsafe { var x: i32 = native::abs(-2) as i32; } }' },
    @{ Name = 'native_unsafe_function'; Code = $native + 'unsafe func helper(): i32 { return native::abs(-2) as i32; } func main(): none { unsafe { var x: i32 = helper(); } }' },
    @{ Name = 'wrapper_local_unsafe'; Code = $native + 'func helper(): i32 { unsafe { return native::abs(-2) as i32; } } func main(): none { var x: i32 = helper(); }' },
    @{ Name = 'caller_does_not_authorize_wrapper_body'; Code = $native + 'func helper(): i32 { return native::abs(-2) as i32; } unsafe func main(): none { var x: i32 = helper(); }'; Error = 'S164' },
    @{ Name = 'unsafe_scope_restored'; Code = $native + 'func main(): none { unsafe { native::abs(-2); } native::abs(-2); }'; Error = 'S164' },
    @{ Name = 'native_condition'; Code = $native + 'func main(): none { if (native::abs(-2) == 2) {} }'; Error = 'S164' },
    @{ Name = 'native_nested_argument'; Code = $native + 'func take(value: i32): none {} func main(): none { take(native::abs(-2) as i32); }'; Error = 'S164' },
    @{ Name = 'native_arity_still_checked'; Code = $native + 'func main(): none { unsafe { native::abs(); } }'; Error = 'S160' },
    @{ Name = 'native_repeat_contract'; Code = $native + 'extern native("stdlib.h") { func abs(number: c_int): c_int; } unsafe func main(): none { native::abs(-2); }' },
    @{ Name = 'safe_console_wrapper'; Code = 'use console; func main(): none { console::print_line("[PASS] console_wrapper"); }'; Pass = $true },
    @{ Name = 'unbounded_input_requires_unsafe'; Code = 'use console; func main(): none { console::read_line("invalid mutable buffer"); }'; Error = 'S164' },
    @{ Name = 'unbounded_input_explicit_contract'; Code = 'use console; unsafe func helper(buffer: str): none { console::read_line(buffer); } func main(): none {}' },
    @{ Name = 'http_fetch_requires_unsafe'; Code = 'use http_client; func main(): none { http_client::fetch("https://example.invalid"); }'; Error = 'S164' },
    @{ Name = 'http_post_requires_unsafe'; Code = 'use http_client; func main(): none { http_client::post("https://example.invalid", "body"); }'; Error = 'S164' },
    @{ Name = 'http_download_requires_unsafe'; Code = 'use http_client; func main(): none { http_client::download("https://example.invalid", "target"); }'; Error = 'S164' },
    @{ Name = 'http_explicit_contract_not_invoked'; Code = 'use http_client; unsafe func helper(): none { http_client::fetch("https://example.invalid"); http_client::post("https://example.invalid", "body"); http_client::download("https://example.invalid", "target"); } func main(): none {}' },
    @{ Name = 'unsafe_still_checks_shared_access'; Code = 'struct Item { value: i32; } func helper(value: &Item): none { unsafe { value.value = 2; } } func main(): none {}'; Error = 'S171' },
    @{ Name = 'unsafe_still_requires_allocation_owner'; Code = 'struct Item { value: i32; } func main(): none { unsafe { var x: i32 = (new Item {}).value; } }'; Error = 'S146' }
)

try {
    foreach ($module in @('console.sg', 'http_client.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$module") -Destination $stdDir
    }
    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]' -or ($case.Pass -and $text -notmatch '\[PASS\]')) {
            throw "$($case.Name): expected successful execution.`n$text"
        }
    }
    Write-Output "Native boundary checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
