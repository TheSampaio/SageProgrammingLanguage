param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

# Incremental native builds: objects are reused only when the generated C, every header it
# includes (generated, prelude, runtime), the Sage compiler version, flags and toolchain are
# unchanged. Every incremental result is compared against a clean (--clean) rebuild.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-incremental-" + [guid]::NewGuid().ToString('N'))
$projectDir = Join-Path $probeDir 'project'
$sourceDir = Join-Path $projectDir 'src'
$objDir = Join-Path $projectDir 'obj'
$exe = Join-Path $projectDir 'bin\project.exe'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Write-Module([string]$Name, [string]$Code) {
    Set-Content -LiteralPath (Join-Path $sourceDir "$Name.sg") -Value $Code
}

function Invoke-Build([string]$Compiler = $compilerPath, [string[]]$Extra = @()) {
    Push-Location $projectDir
    try { $output = & dotnet $Compiler build --verbose @Extra 2>&1 }
    finally { Pop-Location }
    $text = $output -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Build failed.`n$text" }
    $match = [regex]::Match($text, 'Native compile: (\d+) compiled \[([^\]]*)\], (\d+) reused \[([^\]]*)\]; link: (relinked|up to date)')
    if (-not $match.Success) { throw "Missing native build summary.`n$text" }
    $names = if ($match.Groups[2].Value) { @($match.Groups[2].Value -split ', ' | Sort-Object) } else { @() }
    return [pscustomobject]@{ Compiled = ($names -join ','); Linked = ($match.Groups[5].Value -eq 'relinked'); Text = $text }
}

function Invoke-Program {
    $output = & $exe
    return "exit=$LASTEXITCODE;" + ($output -join '|')
}

function Assert-Build($Result, [string]$Expected, [bool]$Linked, [string]$Step) {
    if ($Result.Compiled -ne $Expected) { throw "${Step}: expected compiled [$Expected], got [$($Result.Compiled)].`n$($Result.Text)" }
    if ($Result.Linked -ne $Linked) { throw "${Step}: expected relinked=$Linked.`n$($Result.Text)" }
}

# The incremental program must behave exactly like a clean rebuild of the same sources.
function Assert-MatchesCleanBuild([string]$Step, [string]$ExpectedOutput) {
    $incremental = Invoke-Program
    if ($incremental -ne $ExpectedOutput) { throw "${Step}: unexpected program output '$incremental'." }
    $clean = Invoke-Build -Extra @('--clean')
    Assert-Build $clean 'alpha,beta,main,probe,util' $true "$Step (clean)"
    $cleanOutput = Invoke-Program
    if ($cleanOutput -ne $incremental) { throw "${Step}: clean '$cleanOutput' differs from incremental '$incremental'." }
}

function Get-ObjectTimes {
    $times = @{}
    foreach ($object in Get-ChildItem -LiteralPath $objDir -Filter *.o) { $times[$object.Name] = $object.LastWriteTimeUtc }
    return $times
}

try {
    # main -> alpha, beta, probe; alpha -> util; beta -> util. Only probe includes a runtime header.
    Write-Module 'main' @'
use alpha; use beta; use probe;
extern io("stdio.h") { func printf(format: str, value: c_int): c_int; }
extern native("stdlib.h") { func exit(code: c_int): none; }
unsafe func main(): none {
    io::printf("alpha=%d\n", alpha::value() as c_int);
    io::printf("beta=%d\n", beta::value() as c_int);
    native::exit((alpha::value() + beta::value() + probe::zero()) as c_int);
}
'@
    Write-Module 'alpha' 'use util; module alpha { func value(): i32 { return util::scale(2); } }'
    Write-Module 'beta' 'use util; module beta { func value(): i32 { return util::scale(3) + 1; } }'
    Write-Module 'util' 'module util { func scale(value: i32): i32 { return value * 4; } }'
    Write-Module 'probe' 'extern rt("sage_byte_buffer.h") { } module probe { func zero(): i32 { return 0; } }'

    $first = Invoke-Build
    Assert-Build $first 'alpha,beta,main,probe,util' $true 'initial build'
    Assert-MatchesCleanBuild 'initial build' 'exit=21;alpha=8|beta=13'

    # No change: nothing compiles, nothing links, objects are untouched.
    $before = Get-ObjectTimes
    $noChange = Invoke-Build
    Assert-Build $noChange '' $false 'no-change rebuild'
    $after = Get-ObjectTimes
    foreach ($name in $before.Keys) { if ($before[$name] -ne $after[$name]) { throw "no-change rebuild rewrote $name." } }
    if ((Invoke-Program) -ne 'exit=21;alpha=8|beta=13') { throw 'no-change rebuild changed the program.' }

    # Normal builds stay quiet about the cache.
    Push-Location $projectDir
    try { $quiet = (& dotnet $compilerPath build 2>&1) -join "`n" } finally { Pop-Location }
    if ($quiet -match 'Native compile') { throw "Cache summary must only appear with --verbose.`n$quiet" }

    # Body-only change: util.h is unchanged, so only util recompiles.
    Write-Module 'util' 'module util { func scale(value: i32): i32 { return value * 5; } }'
    Assert-Build (Invoke-Build) 'util' $true 'body-only change'
    Assert-MatchesCleanBuild 'body-only change' 'exit=26;alpha=10|beta=16'

    # Public interface change in beta: beta.h changes, so beta and its dependent main recompile.
    Write-Module 'beta' 'use util; module beta { func value(): i32 { return util::scale(3) + 2; } func extra(): i32 { return 0; } }'
    Assert-Build (Invoke-Build) 'beta,main' $true 'beta interface change'
    Assert-MatchesCleanBuild 'beta interface change' 'exit=27;alpha=10|beta=17'

    # Interface change in util reaches every module through the include closure.
    Write-Module 'util' 'module util { func scale(value: i32): i32 { return value * 5; } func unused(): i32 { return 1; } }'
    Assert-Build (Invoke-Build) 'alpha,beta,main,util' $true 'util interface change'
    Assert-MatchesCleanBuild 'util interface change' 'exit=27;alpha=10|beta=17'

    # Missing or tampered objects and a missing executable are detected.
    Remove-Item -LiteralPath (Join-Path $objDir 'util.o')
    Add-Content -LiteralPath (Join-Path $objDir 'alpha.o') -Value 'tampered'
    # GCC reproduces byte-identical objects, so the link inputs are unchanged and no relink is needed.
    Assert-Build (Invoke-Build) 'alpha,util' $false 'missing and tampered objects'
    Remove-Item -LiteralPath $exe
    Assert-Build (Invoke-Build) '' $true 'missing executable'
    if ((Invoke-Program) -ne 'exit=27;alpha=10|beta=17') { throw 'relinked executable misbehaved.' }

    # A corrupted manifest falls back to a clean rebuild and is rewritten.
    Set-Content -LiteralPath (Join-Path $objDir 'sage-build-cache.json') -Value '{ not json'
    $recovered = Invoke-Build
    Assert-Build $recovered 'alpha,beta,main,probe,util' $true 'corrupted manifest'
    if ($recovered.Text -notmatch 'manifest was unreadable') { throw "Missing corrupted-manifest notice.`n$($recovered.Text)" }
    Assert-Build (Invoke-Build) '' $false 'after manifest recovery'
    Assert-MatchesCleanBuild 'corrupted manifest' 'exit=27;alpha=10|beta=17'

    # A relocated compiler with identical runtime headers reuses everything (content addressed);
    # editing one of its runtime headers recompiles exactly the units that include it (probe, main).
    $copyDir = Join-Path $probeDir 'compiler-copy'
    Copy-Item -LiteralPath (Split-Path $compilerPath) -Destination $copyDir -Recurse
    $copiedCompiler = Join-Path $copyDir (Split-Path $compilerPath -Leaf)
    Assert-Build (Invoke-Build $copiedCompiler) '' $false 'relocated compiler'
    Add-Content -LiteralPath (Join-Path $copyDir 'Runtime\include\sage_byte_buffer.h') -Value '/* changed */'
    Assert-Build (Invoke-Build $copiedCompiler) 'main,probe' $false 'runtime header change'

    # A different Sage compiler version invalidates every object. The copy's version literal is
    # patched in place (same length) so the check runs end to end without rebuilding the compiler.
    $version = [regex]::Match(((& dotnet $compilerPath --version) -join ''), '\d+\.\d+\.\d+').Value
    $patched = ($(if ($version[0] -eq '9') { '8' } else { '9' })) + $version.Substring(1)
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $dllText = $latin1.GetString([IO.File]::ReadAllBytes($copiedCompiler))
    $from = $latin1.GetString([Text.Encoding]::Unicode.GetBytes($version))
    $to = $latin1.GetString([Text.Encoding]::Unicode.GetBytes($patched))
    [IO.File]::WriteAllBytes($copiedCompiler, $latin1.GetBytes($dllText.Replace($from, $to)))
    if (((& dotnet $copiedCompiler --version) -join '') -notmatch [regex]::Escape($patched)) { throw 'Version patch did not take effect.' }
    Assert-Build (Invoke-Build $copiedCompiler) 'alpha,beta,main,probe,util' $false 'compiler version change'
    Assert-Build (Invoke-Build $copiedCompiler) '' $false 'after compiler version change'

    Write-Output 'Incremental build checks passed.'
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
