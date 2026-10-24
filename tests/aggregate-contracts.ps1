param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-aggregates-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } struct Pair { left: Item; right: Item; } '
$cases = @(
    @{ Name = 'wrong_field'; Code = 'func main(): none { var x: Item = { value = "wrong" }; }'; Error = 'S181' },
    @{ Name = 'wrong_new_field'; Code = 'func main(): none { var x: Item = new Item { value = "wrong" }; }'; Error = 'S181' },
    @{ Name = 'unknown_field'; Code = 'func main(): none { var x: Item = { missing = 1 }; }'; Error = 'S131' },
    @{ Name = 'nested_wrong_field'; Code = 'func main(): none { var x: Pair = { left = { value = "wrong" } }; }'; Error = 'S181' },
    @{ Name = 'scalar_named_initializer'; Code = 'func main(): none { var x: i32 = { value = 1 }; }'; Error = 'S180' },
    @{ Name = 'scalar_array_initializer'; Code = 'func main(): none { var x: i32 = { 1, 2 }; }'; Error = 'S180' },
    @{ Name = 'struct_positional_initializer'; Code = 'func main(): none { var x: Item = { 1 }; }'; Error = 'S180' },
    @{ Name = 'array_named_initializer'; Code = 'func main(): none { var x: i32[2] = { value = 1 }; }'; Error = 'S180' },
    @{ Name = 'wrong_array_element'; Code = 'func main(): none { var x: i32[2] = { 1, "wrong" }; }'; Error = 'S181' },
    @{ Name = 'excess_array_elements'; Code = 'func main(): none { var x: i32[1] = { 1, 2 }; }'; Error = 'S182' },
    @{ Name = 'wrong_assignment_field'; Code = 'func main(): none { var x: Item = { value = 1 }; x = { value = "wrong" }; }'; Error = 'S181' },
    @{ Name = 'wrong_call_field'; Code = 'func take(x: Item): none {} func main(): none { take({ value = "wrong" }); }'; Error = 'S181' },
    @{ Name = 'wrong_return_field'; Code = 'func make(): Item { return { value = "wrong" }; } func main(): none {}'; Error = 'S181' },
    @{ Name = 'duplicate_literal_field'; Code = 'func main(): none { var x: Item = { value = 1, value = 2 }; }'; Error = 'S183' },
    @{ Name = 'duplicate_new_field'; Code = 'func main(): none { var x: Item = new Item { value = 1, value = 2 }; }'; Error = 'S183' },
    @{ Name = 'duplicate_layout_field'; Code = 'struct Invalid { x: i32; x: i32; } func main(): none {}'; Error = 'S183' },
    @{ Name = 'owner_in_field'; Code = 'func main(): none { var owner: Item = new Item {}; var pair: Pair = { left = owner }; }'; Error = 'S144' },
    @{ Name = 'owner_in_array'; Code = 'func main(): none { var owner: Item = new Item {}; var values: Item[1] = { owner }; }'; Error = 'S144' },
    @{ Name = 'empty_struct'; Code = 'func main(): none { var x: Item = {}; x = {}; }' },
    @{ Name = 'partial_struct'; Code = 'func main(): none { var x: Pair = { left = { value = 2 } }; }' },
    @{ Name = 'nested_new'; Code = 'func main(): none { var x: Pair = new Pair { left = { value = 2 } }; }' },
    @{ Name = 'struct_array'; Code = 'func main(): none { var x: Item[2] = { { value = 1 }, { value = 2 } }; }' },
    @{ Name = 'literal_promotion'; Code = 'struct Wide { value: i64; } func main(): none { var x: Wide = { value = 7 }; }' },
    @{ Name = 'ffi_field_requires_cast'; Code = 'struct Native { value: c_int; } func main(): none { var n: c_int = 1; var x: Item = { value = n }; }'; Error = 'S181' },
    @{ Name = 'ffi_field_cast'; Code = 'func main(): none { var n: c_int = 1; var x: Item = { value = n as i32 }; }' },
    @{ Name = 'array_field'; Code = 'struct Buffer { data: i32[2]; } func main(): none { var x: Buffer = { data = { 1, 2 } }; }' },
    @{ Name = 'empty_array'; Code = 'func main(): none { var x: i32[2] = {}; }' }
)

try {
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
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
    Write-Output "Aggregate contract checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
