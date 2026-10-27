param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

# The module dependency graph is derived from `use` declarations: discovery order for
# modules, declaration order for edges, duplicate edges collapsed, cycles tolerated.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-graph-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'main.sg') -Value @'
use alpha; use beta; use alpha;
extern io("stdio.h") { func printf(format: str, value: c_int): c_int; }
unsafe func main(): none { io::printf("graph=%d\n", (alpha::value() + beta::value()) as c_int); }
'@
    # alpha <-> beta is a cycle; both reach util (a diamond).
    Set-Content -LiteralPath (Join-Path $sourceDir 'alpha.sg') -Value 'use util; use beta; module alpha { func value(): i32 { return util::one(); } func other(): i32 { return beta::base(); } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'beta.sg') -Value 'use alpha; use util; module beta { func base(): i32 { return 40; } func value(): i32 { return alpha::value() + util::one(); } }'
    Set-Content -LiteralPath (Join-Path $sourceDir 'util.sg') -Value 'module util { func one(): i32 { return 1; } }'

    $expected = 'Module graph: main -> alpha, beta; alpha -> util, beta; beta -> alpha, util; util -> (none)'
    $main = Join-Path $sourceDir 'main.sg'
    for ($i = 0; $i -lt 2; $i++) {
        $output = & dotnet $compilerPath --verbose $main 2>&1
        $text = $output -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Cyclic multi-module build failed.`n$text" }
        if (-not $text.Contains($expected)) { throw "Unexpected module graph (run $i).`n$text" }
        if ($text -notmatch 'graph=3') { throw "Unexpected program output (run $i).`n$text" }
    }

    $quiet = (& dotnet $compilerPath $main 2>&1) -join "`n"
    if ($quiet -match 'Module graph') { throw "The module graph must only be printed with --verbose.`n$quiet" }

    Write-Output 'Module graph checks passed.'
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
