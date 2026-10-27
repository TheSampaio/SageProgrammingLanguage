param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-borrow-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } func read(value: &Item): i32 { return value.value; } func write(value: &mut Item): none { value.value = 9; } '
$cases = @(
    @{ Name = 'shared_owner'; Code = 'unsafe func main(): none { var owner: Item = new Item { value = 7 }; var x: i32 = read(&owner); owner.value = x; }' },
    @{ Name = 'mutable_stack'; Code = 'func main(): none { var value: Item = { value = 1 }; write(&mut value); var x: i32 = value.value; }' },
    @{ Name = 'scalar'; Code = 'func increment(value: &mut i32): none { value++; } func main(): none { var x: i32 = 1; increment(&mut x); }' },
    @{ Name = 'shared_aliases'; Code = 'func pair(a: &Item, b: &Item): i32 { return a.value + b.value; } func main(): none { var x: Item = { value = 1 }; var n: i32 = pair(&x, &x); }' },
    @{ Name = 'mutable_reborrow'; Code = 'func relay(value: &mut Item): none { write(&mut value); } func main(): none { var x: Item = { value = 1 }; relay(&mut x); }' },
    @{ Name = 'shared_reborrow'; Code = 'func relay(value: &mut Item): i32 { return read(&value); } func main(): none { var x: Item = { value = 1 }; var n: i32 = relay(&mut x); }' },
    @{ Name = 'shared_mutation'; Code = 'func bad(value: &Item): none { value.value = 2; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'shared_increment'; Code = 'func bad(value: &i32): none { value++; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'mutable_constant'; Code = 'func main(): none { const x: Item = { value = 1 }; write(&mut x); }'; Error = 'S171' },
    @{ Name = 'shared_to_mutable'; Code = 'func bad(value: &Item): none { write(&mut value); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'conflicting_borrows'; Code = 'func pair(a: &mut Item, b: &Item): none {} func main(): none { var x: Item = { value = 1 }; pair(&mut x, &x); }'; Error = 'S172' },
    @{ Name = 'two_mutable'; Code = 'func pair(a: &mut Item, b: &mut Item): none {} func main(): none { var x: Item = { value = 1 }; pair(&mut x, &mut x); }'; Error = 'S172' },
    @{ Name = 'distinct_mutable'; Code = 'func pair(a: &mut Item, b: &mut Item): none { a.value = 1; b.value = 2; } func main(): none { var x: Item = { value = 0 }; var y: Item = { value = 0 }; pair(&mut x, &mut y); }' },
    @{ Name = 'borrowed_delete'; Code = 'unsafe func bad(value: &Item): none { delete value; } unsafe func main(): none {}'; Error = 'S174' },
    @{ Name = 'borrowed_return_type'; Code = 'func bad(): &i32 { var x: i32 = 1; return &x; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'borrowed_local_binding'; Code = 'func main(): none { var x: i32 = 1; var y: &i32 = &x; }' },
    @{ Name = 'borrowed_field'; Code = 'struct Bad { value: &i32; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'temporary'; Code = 'unsafe func main(): none { var x: i32 = read(&new Item {}); }'; Error = 'S175' },
    @{ Name = 'field_place'; Code = 'func scalar(value: &i32): i32 { return value; } func main(): none { var x: Item = { value = 1 }; var n: i32 = scalar(&x.value); }'; Error = 'S175' },
    @{ Name = 'missing_borrow_expression'; Code = 'func main(): none { var x: Item = { value = 1 }; var n: i32 = read(x); }'; Error = 'S170' },
    @{ Name = 'wrong_borrow_mode'; Code = 'func main(): none { var x: Item = { value = 1 }; write(&x); }'; Error = 'S170' },
    @{ Name = 'borrow_after_move'; Code = 'unsafe func main(): none { var x: Item = new Item {}; var y: Item = x; var n: i32 = read(&x); }'; Error = 'S140' },
    @{ Name = 'borrow_after_delete'; Code = 'unsafe func main(): none { var x: Item = new Item {}; delete x; var n: i32 = read(&x); }'; Error = 'S141' },
    @{ Name = 'constant_increment'; Code = 'func main(): none { const x: i32 = 1; x++; }'; Error = 'S103' },
    @{ Name = 'wrong_borrowed_type'; Code = 'func main(): none { var x: i32 = 1; var n: i32 = read(&x); }'; Error = 'S170' },
    @{ Name = 'nested_shared_mutation'; Code = 'struct Outer { item: Item; } func bad(value: &Outer): none { value.item.value = 2; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'borrow_escape_expression'; Code = 'func bad(): i32 { var x: i32 = 1; return &x; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'native_borrow_contract'; Code = 'extern c("stdlib.h") { func abs(value: &i32): c_int; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'global_borrow'; Code = 'var x: Item = { value = 1 }; func main(): none { var n: i32 = read(&x); }'; Error = 'S176' },
    @{ Name = 'string_borrow_contract'; Code = 'func bad(value: &str): none {} func main(): none {}'; Error = 'S176' },
    @{ Name = 'array_borrow_contract'; Code = 'func bad(value: &i32[2]): none {} func main(): none {}'; Error = 'S176' },
    @{ Name = 'borrow_with_effectful_argument'; Code = 'func number(): i32 { return 1; } func take(value: &Item, n: i32): none {} func main(): none { var x: Item = { value = 1 }; take(&x, number()); }'; Error = 'S177' },
    @{ Name = 'copy_borrowed_value'; Code = 'func copy(value: &Item): Item { return value; } func main(): none { var x: Item = { value = 1 }; var y: Item = copy(&x); }' },
    @{ Name = 'forward_struct'; Code = 'func later(value: &Later): i32 { return value.number; } struct Later { number: i32; } func main(): none { var x: Later = { number = 1 }; var n: i32 = later(&x); }' },
    @{ Name = 'borrow_in_loop'; Code = 'unsafe func main(): none { var x: Item = new Item {}; for (var i: i32 = 0; i < 2; i++) { write(&mut x); } }' },
    @{ Name = 'contextual_mut_name'; Code = 'func increment(value: &mut i32): none { value++; } func main(): none { var mut: i32 = 1; increment(&mut mut); }' },
    @{ Name = 'shared_constant'; Code = 'func main(): none { const x: Item = { value = 1 }; var n: i32 = read(&x); }' },
    @{ Name = 'reborrow_conflict'; Code = 'func pair(a: &mut Item, b: &Item): none {} func bad(value: &mut Item): none { pair(&mut value, &value); } func main(): none {}'; Error = 'S172' },
    @{ Name = 'value_argument_capture'; Code = 'unsafe func add(value: &mut Item, delta: i32): none { value.value = value.value + delta; } unsafe func main(): none { var x: Item = new Item { value = 2 }; add(&mut x, x.value); }' }
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
    Write-Output "Borrowing checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
