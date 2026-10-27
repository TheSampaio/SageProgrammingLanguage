param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-struct-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$layout = 'struct Box { item: OwnedFile; value: i32; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$owner = 'var owner: OwnedFile = make(); '
$box = $owner + 'var box: Box = { item = owner }; '
$cases = @(
    @{ Name = 'local_struct'; Code = $box },
    @{ Name = 'empty_struct'; Code = 'var box: Box = {};' },
    @{ Name = 'use_after_field_move'; Code = $box + 'owner;'; Error = 'S140' },
    @{ Name = 'duplicate_field_move'; Code = 'var owner: OwnedFile = make(); var pair: Pair = { first = owner, second = owner };'; Error = 'S140' },
    @{ Name = 'move_struct'; Code = $box + 'var moved: Box = box;' },
    @{ Name = 'use_after_struct_move'; Code = $box + 'var moved: Box = box; var value: i32 = box.value;'; Error = 'S140' },
    @{ Name = 'reset_struct'; Code = $box + 'box = {};' },
    @{ Name = 'assign_resource_field'; Code = $box + 'var next: OwnedFile = make(); box.item = next;' },
    @{ Name = 'replaced_source_unavailable'; Code = $box + 'var next: OwnedFile = make(); box.item = next; next;'; Error = 'S140' },
    @{ Name = 'replaced_source_borrowed'; Code = $box + 'var next: OwnedFile = make(); var view: &OwnedFile = &next; box.item = view;'; Error = 'S191' },
    @{ Name = 'replaced_source_live_loan'; Code = $box + 'var next: OwnedFile = make(); var view: &OwnedFile = &next; box.item = next;'; Error = 'S178' },
    @{ Name = 'replace_const_struct_field'; Code = $owner + 'const box: Box = { item = owner }; var next: OwnedFile = make(); box.item = next;'; Error = 'S103' },
    @{ Name = 'replace_with_anonymous_result'; Code = $box + 'box.item = make();'; Error = 'S143' },
    @{ Name = 'replace_inside_expression'; Code = $box + 'var next: OwnedFile = make(); var result: i32 = (box.item = next);'; Error = 'S143' },
    @{ Name = 'replace_from_outer_loop'; Code = $box + 'var next: OwnedFile = make(); for (var i: i32 = 0; i < 1; i++) { box.item = next; }'; Error = 'S145' },
    @{ Name = 'anonymous_field_result'; Code = 'var box: Box = { item = make() };'; Error = 'S196' },
    @{ Name = 'borrowed_field_source'; Code = $owner + 'var view: &OwnedFile = &owner; var box: Box = { item = view };'; Error = 'S191' },
    @{ Name = 'live_borrow_on_field_move'; Code = $owner + 'var view: &OwnedFile = &owner; var box: Box = { item = owner };'; Error = 'S178' },
    @{ Name = 'resource_field_array'; Code = 'var values: Box[1] = { {} };' },
    @{ Name = 'declared_nested_resource_struct'; Code = 'struct Outer { box: Box; }' },
    @{ Name = 'resource_struct_pointer_field'; Code = 'struct Outer { box: Box*; }'; Error = 'S196' },
    @{ Name = 'global_resource_struct'; Code = 'var global: Box = {};'; Error = 'S196'; Global = $true },
    @{ Name = 'resource_struct_parameter'; Code = 'func take(box: Box): none {}'; Global = $true },
    @{ Name = 'resource_struct_return'; Code = 'func create(): Box { return {}; }'; Global = $true },
    @{ Name = 'resource_struct_borrow_parameter'; Code = 'func view(box: &Box): none {}'; Error = 'S196'; Global = $true },
    @{ Name = 'resource_struct_new'; Code = 'var box: Box = new Box {};'; Error = 'S196' },
    @{ Name = 'resource_struct_cast'; Code = $box + 'unsafe { var raw: Box* = box as Box*; }'; Error = 'S196' }
)

try {
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_contract.sg') -Value $contract
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_box.sg') -Value ('use resource_contract; ' + $layout)
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-struct.sg') -Raw
    $source = Join-Path $probeDir 'resource_struct.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 19) {
        throw "Resource struct runtime checks failed ($passes/19 PASS).`n$text"
    }

    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $code = if ($case.Global) { $case.Code + 'func main(): none {}' } else { 'func main(): none { ' + $case.Code + ' }' }
        Set-Content -LiteralPath $source -Value ($contract + $layout +
            'struct Pair { first: OwnedFile; second: OwnedFile; } ' + $factory + $code)
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
    $imported = Join-Path $probeDir 'imported_contract.sg'
    Set-Content -LiteralPath $imported -Value ('use resource_contract; ' + $layout + $factory +
        'func main(): none { var owner: OwnedFile = make(); var box: Box = { item = owner }; }')
    $output = & dotnet $compilerPath $imported 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match 'error |\[FAIL\]') {
        throw "Imported resource contract did not generate a valid owning struct.`n$($output -join "`n")"
    }
    $importedBox = Join-Path $probeDir 'imported_box.sg'
    Set-Content -LiteralPath $importedBox -Value 'use resource_box; func main(): none { var box: Box = {}; }'
    $output = & dotnet $compilerPath $importedBox 2>&1
    if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch 'error S196:') {
        throw "Imported resource-containing struct must remain outside the local ownership subset.`n$($output -join "`n")"
    }
    Write-Output "Native resource struct checks passed: $passes runtime assertions, $($cases.Count) compiler cases, and two import cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-struct-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-struct test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
