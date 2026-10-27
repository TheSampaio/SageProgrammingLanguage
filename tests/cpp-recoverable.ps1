param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$fixtures = Join-Path $PSScriptRoot 'fixtures'
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-cpp-recoverable-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$stdDir = Join-Path $probeDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null

function Invoke-Sage([string]$Name, [string]$Code) {
    $source = Join-Path $sourceDir ($Name + '.sg')
    Set-Content -LiteralPath $source -Value $Code
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & dotnet $compilerPath $source 2>&1 }
    finally { $ErrorActionPreference = $previousErrorAction }
    return @{ Status = $LASTEXITCODE; Text = ($output -join "`n") }
}

$block = 'extern cpp safe("cpp-recoverable.hpp") { '
$main = ' func main(): none {}'
$negative = @(
    @{ Name = 'status_with_parameter'; Code = $block + 'func failed(value: c_int): b8 = catch; }' + $main; Error = 'S210' },
    @{ Name = 'status_wrong_result'; Code = $block + 'func failed(): c_int = catch; }' + $main; Error = 'S210' },
    @{ Name = 'message_wrong_result'; Code = $block + 'func reason(): b8 = catch.message; }' + $main; Error = 'S210' },
    @{ Name = 'message_with_parameter'; Code = $block + 'func reason(value: c_int): str = catch.message; }' + $main; Error = 'S210' },
    @{ Name = 'unknown_failure_query'; Code = $block + 'func reason(): str = catch.text; }' + $main; Error = 'S001' },
    @{ Name = 'catch_target_injection'; Code = $block + 'func divide(a: c_int, b: c_int): c_int = "safe::divide(0,0); int x" catch; }' + $main; Error = 'S210' },
    @{ Name = 'catch_requires_unsafe'; Code = $block + 'func divide(a: c_int, b: c_int): c_int = safe::divide catch; } func main(): none { var x: i32 = safe::divide(1, 1) as i32; }'; Error = 'S164' },
    @{ Name = 'catch_struct_parameter'; Code = 'struct Pair { a: i32; } ' + $block + 'func take(value: Pair): none = safe::check catch; }' + $main; Error = 'S210' },
    @{ Name = 'recoverable_conflict'; Code = $block + 'func divide(a: c_int, b: c_int): c_int = safe::divide catch; } ' + $block + 'func divide(a: c_int, b: c_int): c_int = safe::divide; }' + $main; Error = 'S152' },
    @{ Name = 'catch_is_c_alias_target'; Code = 'extern native("stdlib.h") { func abs(value: c_int): c_int = catch; }' + $main; Error = 'S001' }
)

try {
    Copy-Item -LiteralPath (Join-Path $fixtures 'cpp-recoverable.hpp'), (Join-Path $fixtures 'cpp-recoverable.sg') -Destination $sourceDir
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/console.sg'), (Join-Path $PSScriptRoot '../Sandbox/std/text.sg') -Destination $stdDir

    # Opted-in bindings return zero results and report the failure instead of aborting.
    $run = Invoke-Sage 'cpp-recoverable' (Get-Content -LiteralPath (Join-Path $fixtures 'cpp-recoverable.sg') -Raw)
    if ($run.Status -ne 0) { throw "Recoverable C++ program must build and exit normally.`n$($run.Text)" }
    foreach ($expected in 'quotient=3', 'divide ok: ok', 'recovered=0', 'divide zero: failed (safe::divide: division by zero)',
        'check unknown: failed (safe::check: unknown C++ exception)', 'check ok: ok', 'open bad: failed (safe::open: negative limit)',
        'no gate', 'open good: ok', 'take 4: ok', 'take 99: failed (safe::take: over limit)',
        'take null: failed (safe::take: null class handle)', 'left=6 over=0 none=0', '[gate dropped 6]') {
        if (!$run.Text.Contains($expected)) { throw "Missing runtime output '$expected'.`n$($run.Text)" }
    }
    # Opt-in only: the failed constructor must not have produced a drop of a never-built object.
    if (([regex]::Matches($run.Text, '\[gate dropped')).Count -ne 1) { throw "Only the live gate may be dropped.`n$($run.Text)" }

    # Without 'catch' the default stays abort: no failure state is generated.
    $abort = Invoke-Sage 'abort_default' ($block + 'func divide(a: c_int, b: c_int): c_int = safe::divide; } func main(): none { unsafe { safe::divide(1, 0); } }')
    if ($abort.Status -eq 0 -or $abort.Text -notmatch 'Sage C\+\+ interop: safe::divide: division by zero') {
        throw "Bindings without catch must still abort.`n$($abort.Text)"
    }
    $adapters = Get-Content -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp') -Raw
    if ($adapters -match 'sage_cpp_failed|sage_cpp_reset') { throw 'Adapters without catch bindings must not carry failure state.' }

    # A failure query without any recoverable binding reports no failure.
    $quiet = Invoke-Sage 'quiet_query' ($block + 'func failed(): b8 = catch; } func main(): none { unsafe { if (safe::failed()) { var x: i32 = 1; } } }')
    if ($quiet.Status -ne 0) { throw "A query without recoverable calls must build and run.`n$($quiet.Text)" }

    foreach ($case in $negative) {
        Remove-Item -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp') -ErrorAction SilentlyContinue
        $result = Invoke-Sage $case.Name $case.Code
        if ($result.Status -ne 1 -or $result.Text -notmatch ('error ' + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c")) -or
            (Test-Path -LiteralPath (Join-Path $probeDir 'obj/sage_cpp_adapters.cpp'))) {
            throw "$($case.Name): expected $($case.Error) before code generation, got status $($result.Status).`n$($result.Text)"
        }
    }
    Write-Output "Recoverable C++ checks passed: 3 runtime programs, $($negative.Count) rejection cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-cpp-recoverable-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside recoverable test directory.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
