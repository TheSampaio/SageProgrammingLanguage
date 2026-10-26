param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-array-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$consumer = 'func take(value: OwnedFile): none {} '
$owner = 'var owner: OwnedFile = make(); '
$array = $owner + 'var items: OwnedFile[2] = { owner }; '
$cases = @(
    @{ Name = 'single_element'; Code = $array },
    @{ Name = 'empty_default'; Code = 'var items: OwnedFile[2];' },
    @{ Name = 'empty_literal'; Code = 'var items: OwnedFile[2] = {};' },
    @{ Name = 'use_after_array_move'; Code = $array + 'owner;'; Error = 'S140' },
    @{ Name = 'duplicate_element_move'; Code = $owner + 'var items: OwnedFile[2] = { owner, owner };'; Error = 'S140' },
    @{ Name = 'borrowed_element_source'; Code = $owner + 'var view: &OwnedFile = &owner; var items: OwnedFile[2] = { view };'; Error = 'S191' },
    @{ Name = 'live_borrow_element_source'; Code = $owner + 'var view: &OwnedFile = &owner; var items: OwnedFile[2] = { owner };'; Error = 'S178' },
    @{ Name = 'anonymous_element'; Code = 'var items: OwnedFile[2] = { make() };'; Error = 'S197' },
    @{ Name = 'array_copy'; Code = $array + 'var copy: OwnedFile[2] = items;'; Error = 'S197' },
    @{ Name = 'array_assignment'; Code = $array + 'items = {};'; Error = 'S188' },
    @{ Name = 'element_assignment'; Code = $array + 'var next: OwnedFile = make(); items[0] = next;'; Error = 'S143' },
    @{ Name = 'element_consume'; Code = $array + 'take(items[0]);'; Error = 'S194' },
    @{ Name = 'array_borrow'; Code = $array + 'var view: &OwnedFile[2] = &items;'; Error = 'S191' },
    @{ Name = 'resource_array_field'; Code = 'struct Box { items: OwnedFile[2]; }'; Error = 'S191'; Global = $true },
    @{ Name = 'resource_array_parameter'; Code = 'func inspect(items: OwnedFile[2]): none {}'; Error = 'S191'; Global = $true },
    @{ Name = 'resource_array_global'; Code = 'var items: OwnedFile[2];'; Error = 'S197'; Global = $true },
    @{ Name = 'resource_array_return'; Code = 'func make_array(): OwnedFile[2] { return {}; }'; Error = 'S188'; Global = $true },
    @{ Name = 'oversized_literal'; Code = 'var items: OwnedFile[1] = { make(), make() };'; Error = 'S182' },
    @{ Name = 'zero_length'; Code = 'var items: OwnedFile[0];'; Error = 'S187' }
)

try {
    $header = (Join-Path $PSScriptRoot 'fixtures/resource-tracker.h').Replace('\', '/')
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/resource-array.sg') -Raw
    $source = Join-Path $probeDir 'resource_array.sg'
    Set-Content -LiteralPath $source -Value $fixture.Replace('@TRACKER_HEADER@', $header)
    $output = & dotnet $compilerPath $source 2>&1
    $text = $output -join "`n"
    $passes = @($output | Select-String '\[PASS\]').Count
    if ($LASTEXITCODE -ne 0 -or $text -match '\[FAIL\]|error ' -or $passes -ne 10) {
        throw "Resource array runtime checks failed ($passes/10 PASS).`n$text"
    }

    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $code = if ($case.Global) { $case.Code + 'func main(): none {}' } else { 'func main(): none { ' + $case.Code + ' }' }
        Set-Content -LiteralPath $source -Value ($contract + $factory + $consumer + $code)
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
    Write-Output "Native resource array checks passed: $passes runtime assertions and $($cases.Count) compiler cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-array-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-array test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
