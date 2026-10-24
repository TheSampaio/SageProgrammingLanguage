param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-loans-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } '
$cases = @(
    @{ Name = 'shared_local'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = &x; var n: i32 = view.value + x.value; }' },
    @{ Name = 'mutable_local'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &mut Item = &mut x; view.value = 2; }' },
    @{ Name = 'owner_read_during_mutable'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &mut Item = &mut x; var n: i32 = x.value; }'; Error = 'S178' },
    @{ Name = 'owner_write_during_shared'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = &x; x.value = 2; }'; Error = 'S178' },
    @{ Name = 'owner_delete_during_shared'; Code = 'func main(): none { var x: Item = new Item {}; var view: &Item = &x; delete x; }'; Error = 'S178' },
    @{ Name = 'owner_move_during_shared'; Code = 'func main(): none { var x: Item = new Item {}; var view: &Item = &x; var next: Item = x; }'; Error = 'S178' },
    @{ Name = 'reuse_after_scope'; Code = 'func main(): none { var x: Item = new Item {}; if (true) { var view: &mut Item = &mut x; view.value = 2; } x.value = 3; }' },
    @{ Name = 'parent_write_during_shared_reborrow'; Code = 'func main(): none { var x: Item = { value = 1 }; var parent: &mut Item = &mut x; var child: &Item = &parent; parent.value = 2; }'; Error = 'S178' },
    @{ Name = 'parent_read_during_shared_reborrow'; Code = 'func main(): none { var x: Item = { value = 1 }; var parent: &mut Item = &mut x; var child: &Item = &parent; var n: i32 = parent.value; }' },
    @{ Name = 'parent_read_during_mutable_reborrow'; Code = 'func main(): none { var x: Item = { value = 1 }; var parent: &mut Item = &mut x; var child: &mut Item = &mut parent; var n: i32 = parent.value; }'; Error = 'S178' },
    @{ Name = 'reborrow_releases_parent'; Code = 'func main(): none { var x: Item = { value = 1 }; var parent: &mut Item = &mut x; if (true) { var child: &mut Item = &mut parent; child.value++; } parent.value++; }' },
    @{ Name = 'cannot_upgrade_shared'; Code = 'func main(): none { var x: Item = { value = 1 }; var parent: &Item = &x; var child: &mut Item = &mut parent; }'; Error = 'S171' },
    @{ Name = 'shared_local_mutation'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = &x; view.value++; }'; Error = 'S171' },
    @{ Name = 'return_copy'; Code = 'func copy(): Item { var x: Item = new Item { value = 2 }; var view: &Item = &x; return view; } func main(): none { var x: Item = copy(); }' },
    @{ Name = 'cannot_rebind_to_inner_scope'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &mut Item = &mut x; if (true) { var inner: Item = { value = 2 }; view = &mut inner; } }'; Error = 'S173' },
    @{ Name = 'shadow_borrow_binding'; Code = 'func main(): none { var x: Item = new Item { value = 2 }; if (true) { var x: &Item = &x; var n: i32 = x.value; } x.value++; }' },
    @{ Name = 'shadow_independent_value'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = &x; if (true) { var x: Item = { value = 2 }; x.value++; } }' },
    @{ Name = 'move_after_borrow_scope'; Code = 'func main(): none { var x: Item = new Item {}; if (true) { var view: &Item = &x; } var next: Item = x; }' },
    @{ Name = 'copy_is_independent'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = &x; var copy: Item = view; copy.value++; }' },
    @{ Name = 'borrow_parameter_local'; Code = 'func read(value: &Item): i32 { var view: &Item = &value; return view.value; } func main(): none { var x: Item = { value = 1 }; var n: i32 = read(&x); }' },
    @{ Name = 'missing_explicit_initializer'; Code = 'func main(): none { var x: Item = { value = 1 }; var view: &Item = x; }'; Error = 'S170' },
    @{ Name = 'conflicting_local_loans'; Code = 'func main(): none { var x: Item = { value = 1 }; var shared: &Item = &x; var mutable: &mut Item = &mut x; }'; Error = 'S178' },
    @{ Name = 'for_initializer_lifetime'; Code = 'func main(): none { var x: Item = { value = 0 }; for (var view: &mut Item = &mut x; view.value < 2; view.value++) {} x.value++; }' },
    @{ Name = 'shadow_scalar_binding'; Code = 'func main(): none { var x: i32 = 1; if (true) { var x: &mut i32 = &mut x; x++; } x++; }' },
    @{ Name = 'local_borrow_passed_to_call'; Code = 'func update(value: &mut Item): none { value.value++; } func main(): none { var x: Item = { value = 1 }; var view: &mut Item = &mut x; update(&mut view); }' },
    @{ Name = 'shadow_borrowed_parameter'; Code = 'func update(value: &mut Item): none { var value: &mut Item = &mut value; value.value++; } func main(): none { var x: Item = { value = 1 }; update(&mut x); }' },
    @{ Name = 'constant_borrow_binding'; Code = 'func main(): none { var x: Item = { value = 1 }; const view: &Item = &x; var n: i32 = view.value; }' },
    @{ Name = 'exclusive_local_alias_read'; Code = 'func main(): none { var x: i32 = 1; var parent: &mut i32 = &mut x; var child: &mut i32 = &mut parent; var n: i32 = parent; }'; Error = 'S178' }
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
    Write-Output "Borrow lifetime checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
