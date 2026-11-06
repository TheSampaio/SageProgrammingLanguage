param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-interp-' + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project with spaces'
$sourceDir = Join-Path $projectDir 'src'
$stdDir = Join-Path $projectDir 'std'
New-Item -ItemType Directory -Path $sourceDir, $stdDir | Out-Null
$cases = @(
    @{ Name = 'argument_position'; Code = 'func show(v: Text): none {} func main(): none { var n: i32 = 1; show($"x{n}"); }'; Error = 'S194' },
    @{ Name = 'expression_statement'; Code = 'func main(): none { var n: i32 = 1; $"x{n}"; }'; Error = 'S214' },
    @{ Name = 'assigned_to_str'; Code = 'func main(): none { var s: str = $"x"; }'; Error = 'S101' },
    @{ Name = 'struct_part'; Code = 'struct P { a: i32; } func main(): none { var p: P = { a = 1 }; var t: Text = $"{p}"; }'; Error = 'S216' },
    @{ Name = 'text_part'; Code = 'func main(): none { var a: Text = text::from("a"); var t: Text = $"{a}"; }'; Error = 'S191' },
    @{ Name = 'pointer_part'; Code = 'func main(): none { var p: i32* = 0 as i32*; var t: Text = $"{p}"; }'; Error = 'S121' },
    @{ Name = 'integer_float_conversion'; Code = 'func main(): none { var n: i32 = 1; var t: Text = $"{n:5f}"; }'; Error = 'S217' },
    @{ Name = 'string_integer_conversion'; Code = 'func main(): none { var s: str = "a"; var t: Text = $"{s:5d}"; }'; Error = 'S217' },
    @{ Name = 'write_back_conversion'; Code = 'func main(): none { var n: i32 = 1; var t: Text = $"{n:n}"; }'; Error = 'S217' },
    @{ Name = 'percent_flag'; Code = 'func main(): none { var n: i32 = 1; var t: Text = $"{n:%d}"; }'; Error = 'S217' },
    @{ Name = 'huge_width'; Code = 'func main(): none { var n: i32 = 1; var t: Text = $"{n:99999}"; }'; Error = 'S217' },
    @{ Name = 'zero_flag_on_string'; Code = 'func main(): none { var s: str = "a"; var t: Text = $"{s:05}"; }'; Error = 'S217' },
    @{ Name = 'empty_expression'; Code = 'func main(): none { var t: Text = $"a{}b"; }'; Error = 'S011' },
    @{ Name = 'unclosed_brace'; Code = 'func main(): none { var t: Text = $"a{n"; }'; Error = 'S010' }
)
try {
    foreach ($name in @('text.sg', 'console.sg')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$name") -Destination $stdDir
    }

    # Behavior plus exactly-once release of every interpolated owner, via the allocation tracker.
    $headerPath = (Join-Path (Split-Path -Parent $compilerPath) 'Runtime/include/sage_text.h').Replace('\', '/')
    $logPath = (Join-Path $probeDir 'allocations.log').Replace('\', '/')
    $trackerPath = (Join-Path $probeDir 'text-tracker.h').Replace('\', '/')
    $tracker = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/allocation-tracker.h') -Raw
    Set-Content -LiteralPath $trackerPath -Value $tracker.Replace('@LOG_PATH@', $logPath).Replace('@HEADER@', $headerPath)
    $modulePath = Join-Path $stdDir 'text.sg'
    $module = Get-Content -LiteralPath $modulePath -Raw
    Set-Content -LiteralPath $modulePath -Value $module.Replace('sage_text.h', $trackerPath)
    $program = Join-Path $sourceDir 'cleanup.sg'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/interpolation-cleanup.sg') -Destination $program
    $output = & dotnet $compilerPath $program 2>&1
    $text = $output -join "`n"
    if ($LASTEXITCODE -ne 0 -or $text -match 'error |Fatal') { throw "Interpolation program failed.`n$text" }
    foreach ($expected in @('item 1', 'item 2', 'rows total=590', 'long=604 capacity=604', 'braces stay {text} here')) {
        if ($text -notmatch [regex]::Escape($expected)) { throw "Missing output '$expected'.`n$text" }
    }
    $generated = Get-Content -LiteralPath (Join-Path $projectDir 'obj/cleanup.c') -Raw
    if ($generated -match 'static char|snprintf') { throw 'Generated C still uses a static interpolation buffer.' }
    $active = @{}
    $allocations = 0; $releases = 0
    foreach ($line in Get-Content -LiteralPath $logPath) {
        $event, $pointer = $line -split ' ', 2
        if ($event -eq 'allocate') {
            if ($active.ContainsKey($pointer)) { throw 'Allocation reused a still-live address.' }
            $active[$pointer] = $true; $allocations++
        } elseif ($event -eq 'free') {
            if (!$active.ContainsKey($pointer)) { throw 'Unknown or duplicate release.' }
            $active.Remove($pointer); $releases++
        }
    }
    # 2 describe results + 100 loop rows + big + two report lines.
    if ($active.Count -ne 0 -or $allocations -ne 105 -or $releases -ne 105) { throw "Interpolation cleanup mismatch: $allocations allocations, $releases releases, $($active.Count) live." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Sandbox/std/text.sg') -Destination $modulePath

    foreach ($case in $cases) {
        $source = Join-Path $sourceDir ($case.Name + '.sg')
        Set-Content -LiteralPath $source -Value ('use text; ' + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':') -or
            (Test-Path -LiteralPath (Join-Path $projectDir "obj/$($case.Name).c"))) { throw "$($case.Name): expected $($case.Error).`n$($output -join "`n")" }
    }
    Write-Output "Interpolation checks passed: behavior, $allocations allocations / $releases frees, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-interp-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
