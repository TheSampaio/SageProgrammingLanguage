param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

# Separate compilation: each generated C module becomes obj/<module>.o, the objects are linked
# with the extern link libraries, and native failures still produce a nonzero exit status.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-native-build-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
$objDir = Join-Path $probeDir 'obj'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Invoke-Sage([string[]]$Arguments) {
    $output = & dotnet $compilerPath @Arguments 2>&1
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($output -join "`n") }
}

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'main.sg') -Value @'
use alpha; use beta;
extern io("stdio.h") { func printf(format: str, value: c_int): c_int; }
extern native("stdlib.h") { func exit(code: c_int): none; }
extern ws("winsock2.h", "ws2_32") { func WSAGetLastError(): c_int; }
unsafe func main(): none {
    ws::WSAGetLastError();
    io::printf("sum=%d\n", (alpha::value() + beta::value()) as c_int);
    native::exit(alpha::value() + beta::value());
}
'@
    Set-Content -LiteralPath (Join-Path $sourceDir 'alpha.sg') -Value 'use util; module alpha { func value(): i32 { return util::scale(2); } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'beta.sg') -Value 'use util; module beta { func value(): i32 { return util::scale(3); } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'util.sg') -Value 'module util { func scale(value: i32): i32 { return value * 4; } }'

    Push-Location $probeDir
    try {
        $build = Invoke-Sage @('build', '--verbose')
        if ($build.Exit -ne 0) { throw "Multi-module build failed.`n$($build.Text)" }
        foreach ($module in 'main', 'alpha', 'beta', 'util') {
            if (-not (Test-Path -LiteralPath (Join-Path $objDir "$module.o"))) { throw "Missing object for $module.`n$($build.Text)" }
        }
        if ($build.Text -notmatch 'Native compile: 4 compiled \[main, alpha, beta, util\]') {
            throw "Expected all four units to be compiled separately.`n$($build.Text)"
        }

        $exe = Join-Path $probeDir ('bin\' + (Split-Path $probeDir -Leaf) + '.exe')
        $runOutput = & $exe
        if ($LASTEXITCODE -ne 20 -or ($runOutput -join "`n") -ne 'sum=20') {
            throw "Linked executable misbehaved: exit $LASTEXITCODE, output '$runOutput'."
        }

        $run = Invoke-Sage @('run')
        if ($run.Exit -ne 20 -or $run.Text -notmatch 'sum=20') { throw "Run must propagate exit 20.`n$($run.Text)" }
    }
    finally { Pop-Location }

    # A native compile failure (missing C header) must surface GCC's diagnostic and fail the build.
    $broken = Join-Path $sourceDir 'broken.sg'
    Set-Content -LiteralPath $broken -Value 'extern missing("sage_missing_probe_header.h") { func probe(): c_int; } func main(): none {}'
    $failure = Invoke-Sage @($broken)
    if ($failure.Exit -eq 0 -or $failure.Text -notmatch 'sage_missing_probe_header\.h') {
        throw "A failing native compile must return nonzero and report the GCC error.`n$($failure.Text)"
    }

    Write-Output 'Native build checks passed.'
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
