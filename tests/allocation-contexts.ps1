param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-allocation-contexts-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } struct Box { item: Item; } '
$cases = @(
    @{ Name = 'projected_return'; Code = 'unsafe func project(): i32 { return (new Item { value = 1 }).value; } unsafe func main(): none { var x: i32 = project(); }'; Error = 'S146' },
    @{ Name = 'projected_initializer'; Code = 'unsafe func main(): none { var x: i32 = (new Item { value = 1 }).value; }'; Error = 'S146' },
    @{ Name = 'projected_statement'; Code = 'unsafe func main(): none { (new Item { value = 1 }).value; }'; Error = 'S146' },
    @{ Name = 'projected_argument'; Code = 'unsafe func take(value: i32): none {} unsafe func main(): none { take((new Item { value = 1 }).value); }'; Error = 'S146' },
    @{ Name = 'projected_comparison'; Code = 'unsafe func main(): none { var yes: b8 = (new Item { value = 1 }).value == 1; }'; Error = 'S146' },
    @{ Name = 'projected_array_element'; Code = 'unsafe func main(): none { var values: i32[1] = { (new Item { value = 1 }).value }; }'; Error = 'S146' },
    @{ Name = 'projected_struct_field'; Code = 'unsafe func main(): none { var value: Item = { value = (new Item { value = 1 }).value }; }'; Error = 'S146' },
    @{ Name = 'nested_allocation_field_projection'; Code = 'unsafe func main(): none { var value: Item = new Item { value = (new Item { value = 1 }).value }; }'; Error = 'S146' },
    @{ Name = 'nested_owning_field'; Code = 'unsafe func main(): none { var value: Box = new Box { item = new Item { value = 1 } }; }'; Error = 'S146' },
    @{ Name = 'anonymous_return'; Code = 'unsafe func make(): Item { return new Item {}; } unsafe func main(): none {}'; Error = 'S146' },
    @{ Name = 'anonymous_argument'; Code = 'unsafe func take(value: Item): none {} unsafe func main(): none { take(new Item {}); }'; Error = 'S146' },
    @{ Name = 'anonymous_delete'; Code = 'unsafe func main(): none { delete new Item {}; }'; Error = 'S146' },
    @{ Name = 'anonymous_unsafe_cast'; Code = 'unsafe func main(): none { unsafe { var raw: none* = (new Item {}) as none*; } }'; Error = 'S146' },
    @{ Name = 'direct_owner'; Code = 'unsafe func main(): none { var owner: Item = new Item { value = 1 }; var value: i32 = owner.value; }' },
    @{ Name = 'parenthesized_owner'; Code = 'unsafe func main(): none { var owner: Item = (new Item { value = 1 }); }' },
    @{ Name = 'constant_owner'; Code = 'unsafe func main(): none { const owner: Item = new Item { value = 1 }; }' },
    @{ Name = 'named_projection_return'; Code = 'unsafe func project(): i32 { var owner: Item = new Item { value = 1 }; return owner.value; } unsafe func main(): none { var value: i32 = project(); }' },
    @{ Name = 'new_with_plain_nested_value'; Code = 'unsafe func main(): none { var owner: Box = new Box { item = { value = 1 } }; }' },
    @{ Name = 'for_initializer_owner'; Code = 'unsafe func main(): none { for (var owner: Item = new Item { value = 1 }; false;) {} }' },
    @{ Name = 'owner_inside_unsafe'; Code = 'unsafe func main(): none { unsafe { var owner: Item = new Item { value = 1 }; var raw: none* = owner as none*; } }' }
)

try {
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful execution.`n$text"
        }
    }
    Write-Output "Allocation context checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
