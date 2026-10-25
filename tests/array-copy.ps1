param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-copy-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$cases = @(
    @{ Name = 'parameter_value_copy'; Code = 'extern io("stdio.h") { func puts(text: str): c_int; } func change(a: i32[2]): none { a[0] = 9; } unsafe func main(): none { var a: i32[2] = { 1, 2 }; change(a); if (a[0] == 1) { io::puts("[PASS] parameter_copy"); } else { io::puts("[FAIL] parameter_alias"); } }'; Pass = $true },
    @{ Name = 'module_parameter_copy'; Code = 'use array_worker; extern io("stdio.h") { func puts(text: str): c_int; } unsafe func main(): none { var a: i32[2] = { 1, 2 }; array_worker::change(a); if (a[0] == 1) { io::puts("[PASS] module_copy"); } else { io::puts("[FAIL] module_alias"); } }'; Pass = $true },
    @{ Name = 'binding_copy'; Code = 'func main(): none { var a: i32[2] = { 1, 2 }; var b: i32[2] = a; b[0]++; }' },
    @{ Name = 'assignment_copy'; Code = 'func main(): none { var a: i32[2] = { 1, 2 }; var b: i32[2]; b = a; b = b; }' },
    @{ Name = 'literal_assignment'; Code = 'func main(): none { var a: i32[2]; a = { 1, 2 }; }' },
    @{ Name = 'literal_argument'; Code = 'func take(a: i32[2]): none {} func main(): none { take({ 1, 2 }); }' },
    @{ Name = 'const_copy'; Code = 'func main(): none { var a: i32[2] = { 1, 2 }; const b: i32[2] = a; var c: i32[2] = b; }' },
    @{ Name = 'const_assignment'; Code = 'func main(): none { var a: i32[2]; const b: i32[2] = { 1, 2 }; b = a; }'; Error = 'S103' },
    @{ Name = 'const_copy_element_write'; Code = 'func main(): none { var a: i32[2]; const b: i32[2] = a; b[0] = 1; }'; Error = 'S103' },
    @{ Name = 'shadow_binding'; Code = 'func main(): none { var a: i32[2] = { 1, 2 }; if (true) { var a: i32[2] = a; a[0]++; } }' },
    @{ Name = 'shadow_parameter'; Code = 'func take(a: i32[2]): none { var a: i32[2] = a; a[0]++; } func main(): none { var a: i32[2]; take(a); }' },
    @{ Name = 'copy_struct_elements'; Code = 'struct Item { value: i32; } func main(): none { var a: Item[2] = { { value = 1 } }; var b: Item[2] = a; b[0].value++; }' },
    @{ Name = 'copy_array_field_assignment'; Code = 'struct Item { data: i32[2]; } func main(): none { var a: Item = {}; var b: i32[2] = { 1, 2 }; a.data = b; }' },
    @{ Name = 'borrow_struct_with_array'; Code = 'struct Item { data: i32[2]; } func change(a: &mut Item): none { a.data[0]++; } func main(): none { var a: Item = {}; change(&mut a); }' },
    @{ Name = 'shared_array_field_write'; Code = 'struct Item { data: i32[2]; } func bad(a: &Item): none { a.data[0]++; } func main(): none {}'; Error = 'S171' },
    @{ Name = 'unchecked_array_field_borrow'; Code = 'struct Item { data: str[2]; } func bad(a: &Item): none {} func main(): none {}'; Error = 'S176' },
    @{ Name = 'copy_wrong_length'; Code = 'func main(): none { var a: i32[2]; var b: i32[3] = a; }'; Error = 'S101' },
    @{ Name = 'text_array_copy'; Code = 'func main(): none { var a: str[2] = { "a", "b" }; var b: str[2] = a; }'; Error = 'S188' },
    @{ Name = 'text_array_parameter'; Code = 'func take(a: str[2]): none {} func main(): none {}'; Error = 'S188' },
    @{ Name = 'array_return'; Code = 'func make(): i32[2] { var a: i32[2]; return a; } func main(): none {}'; Error = 'S188' },
    @{ Name = 'global_array_copy'; Code = 'var a: i32[2] = { 1, 2 }; var b: i32[2] = a; func main(): none {}'; Error = 'S188' },
    @{ Name = 'field_copy_initializer'; Code = 'struct Item { data: i32[2]; } func main(): none { var a: i32[2]; var b: Item = { data = a }; }'; Error = 'S188' },
    @{ Name = 'temporary_field_copy'; Code = 'struct Row { data: i32[2]; } func make(): Row { return { data = { 1, 2 } }; } func main(): none { var a: i32[2] = make().data; a = make().data; }' },
    @{ Name = 'temporary_field_index'; Code = 'struct Row { data: i32[2]; } func make(): Row { return { data = { 1, 2 } }; } func main(): none { var x: i32 = make().data[0]; }'; Error = 'S189' },
    @{ Name = 'temporary_field_write'; Code = 'struct Row { data: i32[2]; } func make(): Row { return { data = { 1, 2 } }; } func main(): none { make().data = { 3, 4 }; }'; Error = 'S189' }
)

try {
    Set-Content -LiteralPath (Join-Path $probeDir 'array_worker.sg') -Value 'module array_worker { func change(a: i32[2]): none { a[0] = 9; } }'
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':')) {
                throw "$($case.Name): expected $($case.Error), got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match '\[FAIL\]|error ' -or ($case.Pass -and $text -notmatch '\[PASS\]')) {
            throw "$($case.Name): expected independent value copies.`n$text"
        }
    }
    Write-Output "Array copy checks passed: $($cases.Count) cases."
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
