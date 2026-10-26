param([Parameter(Mandatory = $true)][string]$CompilerDll)

# `new` and `delete` are manual memory management: rejected in safe code (S133), trusted inside unsafe.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-manual-allocation-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } func read(value: &Item): i32 { return value.value; } '
$cases = @(
    @{ Name = 'safe_new'; Code = 'func main(): none { var item: Item = new Item { value = 1 }; }'; Error = 'S133' },
    @{ Name = 'safe_new_projection'; Code = 'func main(): none { var value: i32 = (new Item {}).value; }'; Error = 'S133' },
    @{ Name = 'safe_new_argument'; Code = 'func main(): none { var value: i32 = read(&new Item {}); }'; Error = 'S175' },
    @{ Name = 'safe_new_in_helper'; Code = 'func helper(): i32 { var item: Item = new Item {}; return item.value; } func main(): none {}'; Error = 'S133' },
    @{ Name = 'safe_delete_after_unsafe_new'; Code = 'func main(): none { unsafe { var item: Item = new Item {}; } var other: Item = {}; delete other; }'; Error = 'S133' },
    @{ Name = 'safe_delete_of_borrow'; Code = 'func drop(value: &mut Item): none { delete value; } func main(): none {}'; Error = 'S133' },
    @{ Name = 'safe_global_new'; Code = 'var global: Item = new Item {}; func main(): none {}'; Error = 'S133' },
    @{ Name = 'safe_new_after_unsafe_block'; Code = 'func main(): none { unsafe { } var item: Item = new Item {}; }'; Error = 'S133' },
    @{ Name = 'unsafe_block_new_delete'; Code = 'func main(): none { unsafe { var item: Item = new Item { value = 2 }; var value: i32 = read(&item); delete item; } }'; Run = $true },
    @{ Name = 'unsafe_func_new'; Code = 'unsafe func make(): i32 { var item: Item = new Item { value = 3 }; return item.value; } func main(): none { unsafe { var value: i32 = make(); } }'; Run = $true },
    @{ Name = 'unsafe_checks_still_apply'; Code = 'func main(): none { unsafe { var item: Item = new Item {}; delete item; delete item; } }'; Error = 'S141' },
    @{ Name = 'safe_value_replacement'; Code = 'func main(): none { var item: Item = { value = 1 }; var copy: Item = item; copy.value = read(&item) + 1; }'; Run = $true }
)

try {
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        $text = $output -join "`n"
        if ($case.Error) {
            if ($LASTEXITCODE -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation.`n$text"
            }
        } elseif ($LASTEXITCODE -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected success.`n$text"
        }
    }
    $message = Join-Path $probeDir 'message.sg'
    Set-Content -LiteralPath $message -Value ($prefix + 'func main(): none { var item: Item = new Item {}; }')
    $text = (& dotnet $compilerPath $message 2>&1) -join "`n"
    if ($text -notmatch "message\.sg\(1,\d+\): error S133: 'new' is manual memory management and requires an 'unsafe'") {
        throw "S133 must report the source location and an actionable message.`n$text"
    }
    Write-Output "Manual allocation checks passed: $($cases.Count) cases and the diagnostic format."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-manual-allocation-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the manual-allocation test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
