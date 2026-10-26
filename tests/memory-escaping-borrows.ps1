param([Parameter(Mandatory = $true)][string]$CompilerDll)

# Every route by which a borrow could outlive or escape its owner must be rejected before C generation.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-escaping-borrows-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$prefix = 'struct Item { value: i32; } ' +
    'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } ' +
    'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } ' +
    'struct Box { item: OwnedFile; } '
$cases = @(
    # Positive controls: copies out of a borrow are independent values.
    @{ Name = 'return_copied_value'; Code = 'func read(value: &Item): Item { return value; } func main(): none { var x: Item = { value = 1 }; var y: Item = read(&x); }' },
    @{ Name = 'borrow_ends_with_scope'; Code = 'func main(): none { var x: Item = {}; if (true) { var view: &mut Item = &mut x; view.value = 1; } x.value = 2; }' },

    # Returning a borrow.
    @{ Name = 'borrowed_return_type'; Code = 'func pass(value: &Item): &Item { return value; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'return_borrow_of_local'; Code = 'func leak(): Item { var x: Item = {}; return &x; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'return_borrowed_resource_as_owner'; Code = 'func leak(value: &OwnedFile): OwnedFile { return value; } func main(): none {}'; Error = 'S191' },

    # Storing a borrow in a field, array, or other value.
    @{ Name = 'borrowed_field_type'; Code = 'struct Holder { view: &Item; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'borrow_in_field_literal'; Code = 'struct Holder { view: Item; } func main(): none { var x: Item = {}; var h: Holder = { view = &x }; }'; Error = 'S173' },
    @{ Name = 'borrow_in_array_literal'; Code = 'func main(): none { var x: Item = {}; var items: Item[1] = { &x }; }'; Error = 'S173' },
    @{ Name = 'borrowed_array_binding'; Code = 'func main(): none { var x: i32 = 1; var views: &i32[1] = &x; }'; Error = 'S170' },
    @{ Name = 'borrowed_resource_into_field'; Code = 'func main(): none { var owner: OwnedFile = make(); var view: &OwnedFile = &owner; var box: Box = { item = view }; }'; Error = 'S191' },
    @{ Name = 'borrowed_resource_into_array'; Code = 'func main(): none { var owner: OwnedFile = make(); var view: &OwnedFile = &owner; var items: OwnedFile[1] = { view }; }'; Error = 'S191' },
    @{ Name = 'borrow_in_interpolation'; Code = 'func main(): none { var x: i32 = 1; var text: str = "{&x}"; }'; Error = 'S173' },

    # Rebinding a borrow to a shorter-lived owner or fabricating one.
    @{ Name = 'rebind_to_inner_scope'; Code = 'func main(): none { var x: Item = {}; var view: &mut Item = &mut x; if (true) { var inner: Item = {}; view = &mut inner; } }'; Error = 'S173' },
    @{ Name = 'cast_creates_borrow'; Code = 'func main(): none { var x: i32 = 1; var same: b8 = (x as &i32) == (x as &i32); }'; Error = 'S173' },
    @{ Name = 'cast_creates_mutable_borrow'; Code = 'func touch(value: &mut i32): none {} func main(): none { var x: i32 = 1; touch(x as &mut i32); }'; Error = 'S170' },
    @{ Name = 'unsafe_cast_creates_borrow'; Code = 'func main(): none { var x: i32 = 1; unsafe { var same: b8 = (x as &i32) == (x as &i32); } }'; Error = 'S173' },
    @{ Name = 'borrow_global'; Code = 'var global: i32 = 1; func main(): none { var view: &i32 = &global; }'; Error = 'S176' },
    @{ Name = 'global_borrow_binding'; Code = 'var global: i32 = 1; var view: &i32 = &global; func main(): none {}'; Error = 'S176' },
    @{ Name = 'borrow_field_place'; Code = 'func main(): none { var x: Item = {}; var view: &i32 = &x.value; }'; Error = 'S175' },
    @{ Name = 'native_borrow_parameter'; Code = 'extern lib("lib.h") { func keep(value: &i32): none; } func main(): none {}'; Error = 'S173' },
    @{ Name = 'native_borrow_return'; Code = 'extern lib("lib.h") { func give(): &i32; } func main(): none {}'; Error = 'S173' },

    # Ending the owner's lifetime while a borrow is live.
    @{ Name = 'move_resource_while_borrowed'; Code = 'func main(): none { var owner: OwnedFile = make(); var view: &OwnedFile = &owner; var next: OwnedFile = owner; }'; Error = 'S178' },
    @{ Name = 'consume_resource_while_borrowed'; Code = 'func take(value: OwnedFile): none {} func main(): none { var owner: OwnedFile = make(); var view: &OwnedFile = &owner; take(owner); }'; Error = 'S178' },
    @{ Name = 'replace_resource_while_borrowed'; Code = 'func main(): none { var owner: OwnedFile = make(); var next: OwnedFile = make(); var view: &OwnedFile = &owner; owner = next; }'; Error = 'S178' },
    @{ Name = 'return_owner_while_borrowed'; Code = 'func give(): OwnedFile { var owner: OwnedFile = make(); var view: &OwnedFile = &owner; return owner; } func main(): none {}'; Error = 'S178' },
    @{ Name = 'overwrite_value_while_borrowed'; Code = 'func main(): none { var x: Item = {}; var view: &Item = &x; x = { value = 2 }; }'; Error = 'S178' },
    @{ Name = 'consume_and_borrow_same_call'; Code = 'func both(value: OwnedFile, view: &OwnedFile): none {} func main(): none { var owner: OwnedFile = make(); both(owner, &owner); }'; Error = 'S194' }
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
    Write-Output "Escaping borrow checks passed: $($cases.Count) cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-escaping-borrows-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the escaping-borrow test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
