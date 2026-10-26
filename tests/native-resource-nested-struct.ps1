param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-nested-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$layout = 'struct Inner { item: OwnedFile; } struct Outer { nested: Inner; last: OwnedFile; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$owner = 'var first: OwnedFile = make(); '
$outer = $owner + 'var outer: Outer = { nested = { item = first } }; '
$cases = @(
    @{ Name = 'nested_literal'; Code = $outer },
    @{ Name = 'nested_empty'; Code = 'var outer: Outer = { nested = {} };' },
    @{ Name = 'nested_source_moved'; Code = $outer + 'first;'; Error = 'S140' },
    @{ Name = 'duplicate_owner'; Code = $owner + 'var outer: Outer = { nested = { item = first }, last = first };'; Error = 'S140' },
    @{ Name = 'copy_nested_binding'; Code = $owner + 'var inner: Inner = { item = first }; var outer: Outer = { nested = inner };'; Error = 'S196' },
    @{ Name = 'copy_outer'; Code = $outer + 'var clone: Outer = outer;'; Error = 'S196' },
    @{ Name = 'assign_outer'; Code = $outer + 'outer = {};'; Error = 'S196' },
    @{ Name = 'nested_field_replacement'; Code = $outer + 'var next: OwnedFile = make(); outer.nested.item = next;' },
    @{ Name = 'nested_replacement_source_moved'; Code = $outer + 'var next: OwnedFile = make(); outer.nested.item = next; next;'; Error = 'S140' },
    @{ Name = 'nested_replacement_borrowed_source'; Code = $outer + 'var next: OwnedFile = make(); var view: &OwnedFile = &next; outer.nested.item = view;'; Error = 'S191' },
    @{ Name = 'nested_replacement_live_loan'; Code = $outer + 'var next: OwnedFile = make(); var view: &OwnedFile = &next; outer.nested.item = next;'; Error = 'S178' },
    @{ Name = 'nested_replacement_const_root'; Code = $owner + 'const outer: Outer = { nested = { item = first } }; var next: OwnedFile = make(); outer.nested.item = next;'; Error = 'S103' },
    @{ Name = 'nested_replacement_anonymous'; Code = $outer + 'outer.nested.item = make();'; Error = 'S143' },
    @{ Name = 'nested_replacement_expression'; Code = $outer + 'var next: OwnedFile = make(); var result: i32 = (outer.nested.item = next);'; Error = 'S143' },
    @{ Name = 'nested_replacement_outer_loop_source'; Code = $outer + 'var next: OwnedFile = make(); for (var i: i32 = 0; i < 1; i++) { outer.nested.item = next; }'; Error = 'S145' },
    @{ Name = 'array_outer'; Code = 'var values: Outer[1] = { {} };' },
    @{ Name = 'borrowed_nested_owner'; Code = $owner + 'var view: &OwnedFile = &first; var outer: Outer = { nested = { item = view } };'; Error = 'S191' },
    @{ Name = 'live_borrow_nested_owner'; Code = $owner + 'var view: &OwnedFile = &first; var outer: Outer = { nested = { item = first } };'; Error = 'S178' },
    @{ Name = 'nested_pointer_field'; Code = 'struct PointerOuter { inner: Inner*; }'; Error = 'S196'; Global = $true },
    @{ Name = 'nested_array_field'; Code = 'struct ArrayOuter { inner: Inner[1]; }'; Error = 'S196'; Global = $true },
    @{ Name = 'forward_nested_type'; Code = 'struct Forward { inner: Later; } struct Later { item: OwnedFile; }'; Error = 'S196'; Global = $true },
    @{ Name = 'self_recursive_resource'; Code = 'struct Recursive { inner: Recursive; item: OwnedFile; }'; Error = 'S196'; Global = $true },
    @{ Name = 'nested_return'; Code = 'func create(): Outer { return {}; }'; Error = 'S196'; Global = $true },
    @{ Name = 'nested_parameter'; Code = 'func take(value: Outer): none {}'; Error = 'S196'; Global = $true }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-nested-struct.sg') -Raw
    $source = Join-Path $probeDir 'resource_nested_struct.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 19) {
        throw "Nested resource runtime checks failed ($passes/19 PASS).`n$text"
    }

    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $code = if ($case.Global) { $case.Code + 'func main(): none {}' } else { 'func main(): none { ' + $case.Code + ' }' }
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
    Write-Output "Nested resource struct checks passed: $passes runtime assertions and $($cases.Count) compiler cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-nested-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the nested-resource test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
