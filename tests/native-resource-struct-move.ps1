param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-struct-move-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$layout = 'struct Box { item: OwnedFile; value: i32; } struct Outer { box: Box; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } ' +
    'func build(): Box { var owner: OwnedFile = make(); return { item = owner }; } ' +
    'func take(box: Box): none {} '
$box = 'var owner: OwnedFile = make(); var box: Box = { item = owner }; '
$cases = @(
    @{ Name = 'move_init'; Code = $box + 'var other: Box = box;' },
    @{ Name = 'move_from_const'; Code = 'var owner: OwnedFile = make(); const box: Box = { item = owner }; var other: Box = box;' },
    @{ Name = 'use_after_move_init'; Code = $box + 'var other: Box = box; var value: i32 = box.value;'; Error = 'S140' },
    @{ Name = 'use_after_parameter_move'; Code = $box + 'take(box); take(box);'; Error = 'S140' },
    @{ Name = 'use_after_assignment_move'; Code = $box + 'var other: Box = {}; other = box; box.value = 1;'; Error = 'S140' },
    @{ Name = 'use_after_nested_move'; Code = $box + 'var outer: Outer = { box = box }; var value: i32 = box.value;'; Error = 'S140' },
    @{ Name = 'use_after_element_move'; Code = $box + 'var items: Box[1] = { box }; take(box);'; Error = 'S140' },
    @{ Name = 'return_move'; Code = 'func give(): Box { var box: Box = {}; return box; } func main(): none { var box: Box = give(); take(box); }'; Raw = $true },
    @{ Name = 'branch_move_then_use'; Code = $box + 'if (box.value == 0) { take(box); } take(box);'; Error = 'S142' },
    @{ Name = 'outer_loop_move'; Code = $box + 'for (var i: i32 = 0; i < 2; i++) { take(box); }'; Error = 'S145' },
    @{ Name = 'self_assignment'; Code = $box + 'box = box;'; Error = 'S143' },
    @{ Name = 'assignment_inside_expression'; Code = $box + 'var other: Box = {}; var result: i32 = (other = box).value;'; Error = 'S143' },
    @{ Name = 'assign_from_call'; Code = $box + 'box = build();'; Error = 'S143' },
    @{ Name = 'reinitialize_moved_owner'; Code = $box + 'var other: Box = box; box = {};'; Error = 'S140' },
    @{ Name = 'const_destination'; Code = 'const box: Box = {}; var other: Box = {}; box = other;'; Error = 'S103' },
    @{ Name = 'discarded_call_result'; Code = 'build();'; Error = 'S193' },
    @{ Name = 'call_result_as_argument'; Code = 'take(build());'; Error = 'S194' },
    @{ Name = 'literal_as_argument'; Code = 'take({});'; Error = 'S194' },
    @{ Name = 'field_projection_of_call'; Code = 'var value: i32 = build().value;'; Error = 'S193' },
    @{ Name = 'return_call_result'; Code = 'func relay(): Box { return build(); } func main(): none {}'; Error = 'S193'; Raw = $true },
    @{ Name = 'entry_point_return'; Code = 'func main(): Box { return {}; }'; Error = 'S191'; Raw = $true },
    @{ Name = 'native_parameter'; Code = 'extern lib("lib.h") { func sink(box: Box): none; } func main(): none { var box: Box = {}; unsafe { lib::sink(box); } }'; Error = 'S196'; Raw = $true },
    @{ Name = 'borrowed_struct_parameter'; Code = 'func view(box: &Box): none {} func main(): none {}'; Error = 'S196'; Raw = $true },
    @{ Name = 'whole_array_copy_still_rejected'; Code = 'var items: Box[1] = {}; var copy: Box[1] = items;'; Error = 'S196' },
    @{ Name = 'struct_copy_into_scalar_context'; Code = $box + 'var text: str = $"{box}";'; Error = 'S196' },
    @{ Name = 'element_replacement'; Code = $box + 'var items: Box[2] = {}; items[1] = box;' },
    @{ Name = 'element_field_replacement'; Code = 'var items: Box[2] = {}; var owner: OwnedFile = make(); items[0].item = owner;' },
    @{ Name = 'resource_element_replacement'; Code = 'var handles: OwnedFile[2] = {}; var owner: OwnedFile = make(); handles[0] = owner;' },
    @{ Name = 'element_replacement_from_call'; Code = 'var items: Box[2] = {}; items[0] = build();'; Error = 'S143' }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-struct-move.sg') -Raw
    $source = Join-Path $probeDir 'resource_struct_move.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 26) {
        throw "Resource struct move runtime checks failed ($passes/26 PASS).`n$text"
    }

    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $code = if ($case.Raw) { $case.Code } else { 'func main(): none { ' + $case.Code + ' }' }
        Set-Content -LiteralPath $source -Value ($contract + $layout + $factory + $code)
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

    Set-Content -LiteralPath (Join-Path $probeDir 'resource_contract.sg') -Value $contract
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_box.sg') -Value ('use resource_contract; ' + $layout +
        'func take(box: Box): none {}')
    $importer = Join-Path $probeDir 'imported_move.sg'
    Set-Content -LiteralPath $importer -Value 'use resource_box; func main(): none { }'
    $output = & dotnet $compilerPath $importer 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -match 'error ') {
        throw "Importing a module with resource struct parameters must compile.`n$($output -join "`n")"
    }
    Set-Content -LiteralPath $importer -Value 'use resource_box; func relay(box: Box): none {} func main(): none { }'
    $output = & dotnet $compilerPath $importer 2>&1
    if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch 'error S196:') {
        throw "An imported resource struct must remain outside the movable same-file subset.`n$($output -join "`n")"
    }
    Write-Output "Resource struct move checks passed: $passes runtime assertions, $($cases.Count) compiler cases, and two import cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-struct-move-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-struct-move test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
