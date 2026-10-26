param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-struct-array-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$layout = 'struct Inner { owner: OwnedFile; } struct Item { inner: Inner; tail: OwnedFile; value: i32; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$owner = 'var owner: OwnedFile = make(); '
$array = $owner + 'var items: Item[2] = { { inner = { owner = owner } } }; '
$cases = @(
    @{ Name = 'literal_struct_array'; Code = $array },
    @{ Name = 'default_empty_array'; Code = 'var items: Item[2];' },
    @{ Name = 'explicit_empty_array'; Code = 'var items: Item[2] = {};' },
    @{ Name = 'empty_element'; Code = 'var items: Item[2] = { {} };' },
    @{ Name = 'moved_owner_unavailable'; Code = $array + 'owner;'; Error = 'S140' },
    @{ Name = 'duplicate_nested_owner'; Code = $owner + 'var items: Item[2] = { { inner = { owner = owner } }, { tail = owner } };'; Error = 'S140' },
    @{ Name = 'named_struct_element_copy'; Code = $owner + 'var item: Item = { inner = { owner = owner } }; var items: Item[2] = { item };'; Error = 'S196' },
    @{ Name = 'anonymous_owner_element'; Code = 'var items: Item[2] = { { tail = make() } };'; Error = 'S196' },
    @{ Name = 'borrowed_nested_owner'; Code = $owner + 'var view: &OwnedFile = &owner; var items: Item[2] = { { inner = { owner = view } } };'; Error = 'S191' },
    @{ Name = 'live_loan_nested_owner'; Code = $owner + 'var view: &OwnedFile = &owner; var items: Item[2] = { { inner = { owner = owner } } };'; Error = 'S178' },
    @{ Name = 'whole_array_copy'; Code = $array + 'var copy: Item[2] = items;'; Error = 'S196' },
    @{ Name = 'whole_array_assignment'; Code = $array + 'items = {};'; Error = 'S196' },
    @{ Name = 'element_replacement'; Code = $array + 'items[0] = {};'; Error = 'S196' },
    @{ Name = 'array_borrow'; Code = $array + 'var view: &Item[2] = &items;'; Error = 'S196' },
    @{ Name = 'array_global'; Code = 'var items: Item[2];'; Error = 'S196'; Global = $true },
    @{ Name = 'array_parameter'; Code = 'func inspect(items: Item[2]): none {}'; Error = 'S196'; Global = $true },
    @{ Name = 'array_return'; Code = 'func make_items(): Item[2] { return {}; }'; Error = 'S188'; Global = $true },
    @{ Name = 'array_struct_field'; Code = 'struct Bag { items: Item[2]; }'; Error = 'S196'; Global = $true },
    @{ Name = 'oversized_literal'; Code = 'var items: Item[1] = { {}, {} };'; Error = 'S182' },
    @{ Name = 'zero_length'; Code = 'var items: Item[0];'; Error = 'S187' }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-struct-array.sg') -Raw
    $source = Join-Path $probeDir 'resource_struct_array.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 12) {
        throw "Resource struct array runtime checks failed ($passes/12 PASS).`n$text"
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
    Write-Output "Native resource struct array checks passed: $passes runtime assertions and $($cases.Count) compiler cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-struct-array-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-struct-array test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
