param([Parameter(Mandatory = $true)][string]$CompilerDll)

# Validates opt-in structured diagnostics (--diagnostics json), unchanged human output,
# quiet success, ownership diagnostics that name the invalidating operation, and docs/catalog sync.
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-diagnostics-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

function Invoke-Sage([string[]]$Arguments) {
    $lines = @(& dotnet $compilerPath @Arguments 2>&1 | ForEach-Object { "$_" })
    [pscustomobject]@{ Exit = $LASTEXITCODE; Lines = $lines; Text = ($lines -join "`n") }
}

function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

function Get-JsonDiagnostics($Result) {
    foreach ($line in $Result.Lines) {
        if ($line.Trim().Length -eq 0) { continue }
        try { $line | ConvertFrom-Json } catch { throw "Non-JSON output line in JSON mode: $line" }
    }
}

try {
    $file = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; }' + "`n" +
        'func take(f: OwnedFile): none {}' + "`n" +
        'unsafe func main(): none {' + "`n" +
        '    var f: OwnedFile = (0 as FILE*) as OwnedFile;' + "`n" +
        '    take(f);' + "`n" +
        '    take(f);' + "`n" + '}'
    $moved = Join-Path $sourceDir 'main.sg'
    Set-Content -LiteralPath $moved -Value $file

    # Human output keeps its established shape and now names the invalidating operation.
    $human = Invoke-Sage @($moved)
    Assert-True ($human.Exit -eq 1) 'Moved-owner use must fail with status 1.'
    Assert-True ($human.Text -match "^main\.sg\(6,10\): error S140: Owner 'f' was moved by passing it to 'take' at \(5,5\) and cannot be used here\.$") "Unexpected human diagnostic:`n$($human.Text)"

    # JSON mode: both flag spellings, one object per line, full field set.
    foreach ($arguments in @(@('--diagnostics', 'json', $moved), @("--diagnostics=json", $moved))) {
        $json = Invoke-Sage $arguments
        Assert-True ($json.Exit -eq 1) 'JSON mode must keep the failure exit status.'
        $items = @(Get-JsonDiagnostics $json)
        Assert-True ($items.Count -eq 1) "Expected exactly one diagnostic, got $($items.Count)."
        $d = $items[0]
        Assert-True ($d.code -eq 'S140' -and $d.severity -eq 'error' -and $d.file -eq 'main.sg') 'Wrong code, severity, or file.'
        Assert-True ($d.line -eq 6 -and $d.column -eq 10) 'Wrong location.'
        Assert-True ($d.path -eq (Resolve-Path -LiteralPath $moved).Path) 'The absolute source path must accompany the file name.'
        Assert-True ($d.message -match "passing it to 'take'") 'The message must name the invalidating operation.'
        Assert-True ($d.rule -match 'moved owner') 'The violated rule must be present.'
        Assert-True ($d.suggestion) 'A reliable suggestion must be present for S140.'
        Assert-True ($d.related.Count -eq 1 -and $d.related[0].symbol -eq 'f' -and $d.related[0].line -eq 5 -and
            $d.related[0].column -eq 5 -and $d.related[0].note -match 'take') 'The related location must point at the invalidating call.'
    }

    # Semantic diagnostics and suggestions; unsafe call has a reliable suggestion.
    Set-Content -LiteralPath $moved -Value 'extern native("stdlib.h") { func abs(v: c_int): c_int; } func main(): none { var x: c_int = native::abs(1); }'
    $unsafeCall = Invoke-Sage @('--diagnostics', 'json', $moved)
    $d = @(Get-JsonDiagnostics $unsafeCall)[0]
    Assert-True ($d.code -eq 'S164' -and $d.suggestion -match 'unsafe') 'S164 must suggest an unsafe context.'

    Set-Content -LiteralPath $moved -Value 'func main(): none { missing(); }'
    $plain = Invoke-Sage @($moved)
    Assert-True ($plain.Text -eq "main.sg(1,21): error S105: Function 'missing' not found.") "Human output must stay byte-identical:`n$($plain.Text)"
    $d = @(Get-JsonDiagnostics (Invoke-Sage @('--diagnostics', 'json', $moved)))[0]
    Assert-True ($d.code -eq 'S105' -and $d.line -eq 1 -and $d.column -eq 21 -and -not $d.suggestion -and -not $d.related) 'S105 must carry no invented suggestion or related data.'

    # Parser, lexer, and import diagnostics.
    Set-Content -LiteralPath $moved -Value 'func main(): none { var x = ; }'
    $d = @(Get-JsonDiagnostics (Invoke-Sage @('--diagnostics', 'json', $moved)))[0]
    Assert-True ($d.code -match '^S00\d$' -and $d.file -eq 'main.sg' -and $d.line -eq 1) 'Parser failures must produce a located JSON diagnostic.'

    Set-Content -LiteralPath $moved -Value 'func main(): none { var s: str = "open; }'
    $items = @(Get-JsonDiagnostics (Invoke-Sage @('--diagnostics', 'json', $moved)))
    Assert-True ($items[0].code -eq 'S013') 'Lexer diagnostics must also be structured.'

    Set-Content -LiteralPath $moved -Value 'use absent_probe_module; func main(): none {}'
    $d = @(Get-JsonDiagnostics (Invoke-Sage @('--diagnostics', 'json', $moved)))[0]
    Assert-True ($d.code -eq 'S007' -and $d.suggestion) 'Unresolved imports must produce S007 with a suggestion.'

    # Non-language failures use S900 without a source file.
    $missing = Invoke-Sage @('--diagnostics', 'json', (Join-Path $probeDir 'absent.sg'))
    $d = @(Get-JsonDiagnostics $missing)[0]
    Assert-True ($missing.Exit -eq 1 -and $d.code -eq 'S900' -and -not $d.file) 'Tool failures must use S900 and omit the file.'

    # Unknown formats are rejected; human remains selectable explicitly.
    $bad = Invoke-Sage @('--diagnostics', 'xml', $moved)
    Assert-True ($bad.Exit -eq 1 -and $bad.Text -match 'Unknown diagnostics format') 'Unknown formats must fail clearly.'
    $explicit = Invoke-Sage @('--diagnostics=human', $moved)
    Assert-True ($explicit.Text -match '^main\.sg\(\d+,\d+\): error S007:') 'Explicit human format must equal the default.'

    # Successful builds stay quiet in JSON mode and keep their normal behavior otherwise.
    Set-Content -LiteralPath $moved -Value 'func main(): none {}'
    Push-Location $probeDir
    try {
        $quiet = Invoke-Sage @('build', '--diagnostics', 'json')
        Assert-True ($quiet.Exit -eq 0 -and $quiet.Text.Trim().Length -eq 0) "JSON mode must stay silent on success.`n$($quiet.Text)"
        $normal = Invoke-Sage @('build')
        Assert-True ($normal.Exit -eq 0 -and $normal.Text -match 'Build complete') 'Human build output must be unchanged.'
    }
    finally { Pop-Location }

    # Every source code is catalogued and documented, and no documented code is stale.
    $sourceCodes = Get-ChildItem -Path (Join-Path $repoRoot 'Core'), (Join-Path $repoRoot 'Utilities') -Recurse -Filter *.cs |
        Where-Object { $_.Name -ne 'DiagnosticCatalog.cs' } |
        ForEach-Object { [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw), '"(S\d{3})"') | ForEach-Object { $_.Groups[1].Value } } |
        Sort-Object -Unique
    $catalogCodes = [regex]::Matches((Get-Content -LiteralPath (Join-Path $repoRoot 'Utilities/Diagnostics/DiagnosticCatalog.cs') -Raw), '\["(S\d{3})"\]') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $docCodes = [regex]::Matches((Get-Content -LiteralPath (Join-Path $repoRoot 'docs/diagnostics.md') -Raw), '(?m)^\|\s*(S\d{3})\s*\|') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $uncatalogued = $sourceCodes | Where-Object { $_ -notin $catalogCodes }
    Assert-True (-not $uncatalogued) "Codes missing from DiagnosticCatalog: $($uncatalogued -join ', ')"
    $undocumented = $catalogCodes | Where-Object { $_ -notin $docCodes }
    Assert-True (-not $undocumented) "Codes missing from docs/diagnostics.md: $($undocumented -join ', ')"
    $stale = $docCodes | Where-Object { $_ -notin $catalogCodes }
    Assert-True (-not $stale) "Documented codes absent from the catalog: $($stale -join ', ')"

    Write-Output 'Structured diagnostics checks passed.'
}
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force }
