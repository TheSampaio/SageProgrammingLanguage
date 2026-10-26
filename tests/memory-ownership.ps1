param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-own-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } '

$cases = @(
    @{ Name = 'move'; Code = 'unsafe func main(): none { var a: Item = new Item { value = 7 }; var b: Item = a; b.value = 8; delete b; }' },
    @{ Name = 'use_after_move'; Code = 'unsafe func main(): none { var a: Item = new Item {}; var b: Item = a; var x: i32 = a.value; }'; Error = 'S140' },
    @{ Name = 'delete_after_move'; Code = 'unsafe func main(): none { var a: Item = new Item {}; var b: Item = a; delete a; }'; Error = 'S140' },
    @{ Name = 'use_after_delete'; Code = 'unsafe func main(): none { var a: Item = new Item {}; delete a; var x: i32 = a.value; }'; Error = 'S141' },
    @{ Name = 'double_delete'; Code = 'unsafe func main(): none { var a: Item = new Item {}; delete a; delete a; }'; Error = 'S141' },
    @{ Name = 'conditional_move'; Code = 'unsafe func main(): none { var a: Item = new Item {}; if (true) { var b: Item = a; delete b; } var x: i32 = a.value; }'; Error = 'S142' },
    @{ Name = 'independent_branches'; Code = 'unsafe func main(): none { var a: Item = new Item {}; if (true) { delete a; } else { delete a; } }' },
    @{ Name = 'both_branches_moved'; Code = 'unsafe func main(): none { var a: Item = new Item {}; if (true) { var b: Item = a; delete b; } else { var c: Item = a; delete c; } delete a; }'; Error = 'S140' },
    @{ Name = 'shadow'; Code = 'unsafe func main(): none { var a: Item = new Item {}; if (true) { var a: Item = new Item {}; delete a; } delete a; }' },
    @{ Name = 'shadow_move'; Code = 'unsafe func main(): none { var a: Item = new Item {}; if (true) { var a: Item = a; delete a; } }' },
    @{ Name = 'returning_branch'; Code = 'unsafe func choose(flag: b8): i32 { var a: Item = new Item {}; if (flag) { delete a; return 1; } var x: i32 = a.value; delete a; return x; } unsafe func main(): none { var x: i32 = choose(false); }' },
    @{ Name = 'overwrite'; Code = 'unsafe func main(): none { var a: Item = new Item {}; a = new Item {}; }'; Error = 'S143' },
    @{ Name = 'missing_parameter_contract'; Code = 'unsafe func take(a: Item): none {} unsafe func main(): none { var a: Item = new Item {}; take(a); }'; Error = 'S144' },
    @{ Name = 'missing_return_contract'; Code = 'unsafe func make(): Item { var a: Item = new Item {}; return a; } unsafe func main(): none {}'; Error = 'S144' },
    @{ Name = 'nested_owner'; Code = 'struct Box { item: Item; } unsafe func main(): none { var a: Item = new Item {}; var b: Box = { item = a }; }'; Error = 'S144' },
    @{ Name = 'outer_owner_in_loop'; Code = 'unsafe func main(): none { var a: Item = new Item {}; while (true) { delete a; } }'; Error = 'S145' },
    @{ Name = 'local_owner_in_loop'; Code = 'unsafe func main(): none { for (var i: i32 = 0; i < 2; i++) { var a: Item = new Item {}; var b: Item = a; delete b; } }' },
    @{ Name = 'copy_struct'; Code = 'func main(): none { var a: Item = { value = 1 }; var b: Item = a; a.value = 2; b.value = 3; }' },
    @{ Name = 'anonymous_allocation'; Code = 'unsafe func main(): none { new Item {}; }'; Error = 'S146' },
    @{ Name = 'global_allocation'; Code = 'var a: Item = new Item {}; unsafe func main(): none {}'; Error = 'S133' },
    @{ Name = 'owner_arithmetic'; Code = 'unsafe func main(): none { var a: Item = new Item {}; a++; }'; Error = 'S144' },
    @{ Name = 'for_assignment_initializer'; Code = 'func main(): none { var i: i32 = 0; for (i = 0; i < 2; i++) {} }' },
    @{ Name = 'rejected_uninitialized'; Code = 'func main(): none { var x: i32; }'; Error = 'S012' }
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
                throw "$($case.Name): expected $($case.Error) and nonzero status, got $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful compilation.`n$text"
        }
    }
    Write-Output "Ownership checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
