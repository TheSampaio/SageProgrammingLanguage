param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$fixtures = Join-Path $PSScriptRoot 'fixtures'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-cpp-interop-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Invoke-Sage([string]$Name, [string]$Code) {
    $source = Join-Path $sourceDir ($Name + '.sg')
    Set-Content -LiteralPath $source -Value $Code
    # Aborting adapters write to stderr, which Windows PowerShell would otherwise raise as an error.
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & dotnet $compilerPath $source 2>&1 }
    finally { $ErrorActionPreference = $previousErrorAction }
    return @{ Status = $LASTEXITCODE; Text = ($output -join "`n") }
}

$block = 'extern cpp geo("cpp-interop.hpp") { class Counter = demo::Counter; '
$main = ' func main(): none {}'
$negative = @(
    @{ Name = 'binding_required'; Code = $block + 'func add(a: c_int, b: c_int): c_int; }' + $main; Error = 'S001' },
    @{ Name = 'opaque_type_rejected'; Code = $block + 'type Raw; }' + $main; Error = 'S006' },
    @{ Name = 'target_injection'; Code = $block + 'func add(a: c_int, b: c_int): c_int = "demo::add(0,0); int x"; }' + $main; Error = 'S210' },
    @{ Name = 'class_target_injection'; Code = 'extern cpp geo("cpp-interop.hpp") { class Counter = "demo::Counter; int"; }' + $main; Error = 'S210' },
    @{ Name = 'struct_parameter'; Code = 'struct Pair { a: i32; } ' + $block + 'func take(value: Pair): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'array_parameter'; Code = $block + 'func take(value: c_int[4]): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'borrow_parameter'; Code = $block + 'func take(value: &c_int): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'none_parameter'; Code = $block + 'func take(value: none): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'handle_double_pointer'; Code = $block + 'func take(value: Counter**): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'handle_by_value'; Code = $block + 'func take(value: Counter): none = demo::take; }' + $main; Error = 'S210' },
    @{ Name = 'constructor_scalar_result'; Code = $block + 'func create(start: c_int): c_int = new; }' + $main; Error = 'S210' },
    @{ Name = 'destructor_extra_parameter'; Code = $block + 'func destroy(value: Counter*, extra: c_int): none = delete; }' + $main; Error = 'S210' },
    @{ Name = 'destructor_result'; Code = $block + 'func destroy(value: Counter*): c_int = delete; }' + $main; Error = 'S210' },
    @{ Name = 'method_without_handle'; Code = $block + 'func value(start: c_int): c_int = .value; }' + $main; Error = 'S210' },
    @{ Name = 'duplicate_class'; Code = $block + 'class Counter = demo::Counter; }' + $main; Error = 'S150' },
    @{ Name = 'call_requires_unsafe'; Code = $block + 'func add(a: c_int, b: c_int): c_int = demo::add; } func main(): none { var x: i32 = geo::add(1, 2) as i32; }'; Error = 'S164' },
    @{ Name = 'conflicting_targets'; Code = $block + 'func add(a: c_int, b: c_int): c_int = demo::add; } extern cpp geo("cpp-interop.hpp") { func add(a: c_int, b: c_int): c_int = demo::biggest; }' + $main; Error = 'S152' }
)

try {
    # 'cpp' stays usable as a C alias, and C-only projects never generate or compile C++ adapters.
    $cAlias = Invoke-Sage 'c_alias' 'extern cpp("stdlib.h") { func abs(value: c_int): c_int; } func main(): none { unsafe { cpp::abs(-2); } }'
    if ($cAlias.Status -ne 0) { throw "A C extern block may still be named 'cpp'.`n$($cAlias.Text)" }
    if (Test-Path -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.*')) { throw 'A C-only build must not produce C++ adapters.' }

    Copy-Item -LiteralPath (Join-Path $fixtures 'cpp-interop.hpp'), (Join-Path $fixtures 'cpp-interop.sg'), (Join-Path $fixtures 'cpp_interop_helper.sg') -Destination $sourceDir

    # Overloads, an explicit template instance, constructor/methods/destructor, resource cleanup,
    # and an adapter shared by two modules.
    $output = & dotnet $compilerPath (Join-Path $sourceDir 'cpp-interop.sg') 2>&1
    $text = $output -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "C++ interop program must build and run.`n$text" }
    foreach ($expected in 'sum=5 real=3.5 top=9', 'sum=42 real=0.5 top=15', '[counter dropped 15]') {
        if (!$text.Contains($expected)) { throw "Missing runtime output '$expected'.`n$text" }
    }
    $adapters = Get-Content -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp') -Raw
    if (([regex]::Matches($adapters, 'sage_cpp_fn_3geo_add\(')).Count -ne 1) { throw 'A binding shared by modules must produce one adapter.' }
    $header = Get-Content -LiteralPath (Join-Path $probeDir 'obj/cpp-interop.h') -Raw
    if ($header -match 'cpp-interop\.hpp') { throw 'C headers must not include the C++ header.' }

    # Exceptions never unwind into C: the adapter reports the Sage binding and aborts.
    $thrower = Invoke-Sage 'exception' 'extern cpp geo("cpp-interop.hpp") { func fail(code: c_int): c_int = demo::fail; } func main(): none { unsafe { geo::fail(1); } }'
    if ($thrower.Status -eq 0 -or $thrower.Text -notmatch 'Sage C\+\+ interop: geo::fail: bad code') {
        throw "Escaping exception must abort with a diagnostic, got status $($thrower.Status).`n$($thrower.Text)"
    }
    $nullHandle = Invoke-Sage 'null_handle' ($block + 'func value(counter: Counter*): c_int = .value; } func main(): none { unsafe { geo::value(0 as Counter*); } }')
    if ($nullHandle.Status -eq 0 -or $nullHandle.Text -notmatch 'geo::value: null class handle') {
        throw "Member calls must reject null handles.`n$($nullHandle.Text)"
    }


    foreach ($case in $negative) {
        Remove-Item -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp') -ErrorAction SilentlyContinue
        $result = Invoke-Sage $case.Name $case.Code
        if ($result.Status -ne 1 -or $result.Text -notmatch ('error ' + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c")) -or
            (Test-Path -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp'))) {
            throw "$($case.Name): expected $($case.Error) before code generation, got status $($result.Status).`n$($result.Text)"
        }
    }
    Write-Output "C++ interop checks passed: 6 runtime checks, $($negative.Count) rejection cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-cpp-interop-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside C++ interop test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
