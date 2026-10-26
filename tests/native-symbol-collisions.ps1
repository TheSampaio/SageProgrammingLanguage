param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$fixtures = Join-Path $PSScriptRoot 'fixtures'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-symbol-collisions-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null

function Invoke-Sage([string]$Name, [string]$Code) {
    $source = Join-Path $sourceDir ($Name + '.sg')
    Set-Content -LiteralPath $source -Value $Code
    $output = & dotnet $compilerPath $source 2>&1
    return @{ Status = $LASTEXITCODE; Text = ($output -join "`n") }
}

$twice = 'module helper { func twice(): none {} } '
$cppAdd = 'extern cpp geo("cpp-interop.hpp") { func add(a: c_int, b: c_int): c_int = demo::add; } '
$negative = @(
    @{ Name = 'flattened_function'; Code = $twice + 'func helper_twice(): none {} func main(): none {}' },
    @{ Name = 'flattened_function_reversed'; Code = 'func helper_twice(): none {} ' + $twice + 'func main(): none {}' },
    @{ Name = 'two_modules'; Code = 'module a_b { func c(): none {} } module a { func b_c(): none {} } func main(): none {}' },
    @{ Name = 'c_extern_vs_module'; Code = $twice + 'extern native("helper.h") { func helper_twice(): none; } func main(): none {}' },
    @{ Name = 'module_vs_c_extern'; Code = 'extern native("helper.h") { func helper_twice(): none; } ' + $twice + 'func main(): none {}' },
    @{ Name = 'c_extern_vs_top_level'; Code = 'extern native("stdlib.h") { func abs(value: c_int): c_int; } func abs(value: i32): i32 { return value; } func main(): none {}' },
    @{ Name = 'adapter_vs_function'; Code = $cppAdd + 'func sage_cpp_fn_3geo_add(): none {} func main(): none {}' },
    @{ Name = 'adapter_vs_function_reversed'; Code = 'func sage_cpp_fn_3geo_add(): none {} ' + $cppAdd + 'func main(): none {}' }
)

try {
    Copy-Item -LiteralPath (Join-Path $fixtures 'cpp-interop.hpp') -Destination $sourceDir

    # Distinct names, the same C function declared through two aliases, and an unrelated module never collide.
    $clean = Invoke-Sage 'clean' ($twice + 'module helper_two { func run(): none {} } extern one("stdlib.h") { func abs(value: c_int): c_int; } extern two("stdlib.h") { func abs(value: c_int): c_int; } func main(): none { unsafe { one::abs(-1); two::abs(-2); } }')
    if ($clean.Status -ne 0) { throw "Distinct symbols and a repeated C function must compile.`n$($clean.Text)" }

    # A collision with an imported module is reported against the second declaration.
    Set-Content -LiteralPath (Join-Path $stdDir 'helper.sg') -Value 'module helper { func twice(): none {} }'
    $imported = Invoke-Sage 'imported' 'use helper; func helper_twice(): none {} func main(): none {}'
    if ($imported.Status -ne 1 -or $imported.Text -notmatch 'error S212:') { throw "Imported module collision must be S212.`n$($imported.Text)" }

    foreach ($case in $negative) {
        $result = Invoke-Sage $case.Name $case.Code
        if ($result.Status -ne 1 -or $result.Text -notmatch 'error S212:.*symbol' -or
            (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c")) -or
            (Test-Path -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp'))) {
            throw "$($case.Name): expected S212 before code generation, got status $($result.Status).`n$($result.Text)"
        }
    }
    Write-Output "Native symbol collision checks passed: 2 positive programs, $($negative.Count + 1) rejection cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-symbol-collisions-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside collision test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
