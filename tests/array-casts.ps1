param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-array-casts-' + [guid]::NewGuid().ToString('N'))
$cases = @(
    @{ Name = 'integer_as_array_argument'; Code = 'func take(value: u8[260]): none {} func main(): none { take(0 as u8[260]); }'; Error = 'S188' },
    @{ Name = 'integer_as_array_binding'; Code = 'func main(): none { var value: u8[2] = 0 as u8[2]; }'; Error = 'S188' },
    @{ Name = 'pointer_as_array_unsafe'; Code = 'unsafe func main(): none { var pointer: u8* = 0 as u8*; var value: u8[2] = pointer as u8[2]; }'; Error = 'S188' },
    @{ Name = 'array_as_array'; Code = 'func main(): none { var value: u8[2]; var copy: u8[2] = value as u8[2]; }'; Error = 'S188' },
    @{ Name = 'array_as_scalar'; Code = 'func main(): none { var value: u8[2]; var number: u64 = value as u64; }'; Error = 'S188' },
    @{ Name = 'array_as_text'; Code = 'func main(): none { var value: u8[2]; var text: str = value as str; }'; Error = 'S188' },
    @{ Name = 'array_as_text_unsafe'; Code = 'unsafe func main(): none { var value: u8[2]; var text: str = value as str; }'; Error = 'S188' },
    @{ Name = 'array_pointer_requires_unsafe'; Code = 'func main(): none { var value: u8[2]; value as c_void*; }'; Error = 'S122' },
    @{ Name = 'array_pointer_explicit_native_boundary'; Code = 'unsafe func main(): none { var value: u8[2]; var pointer: c_void* = value as c_void*; }' },
    @{ Name = 'scalar_cast_and_array_copy'; Code = 'func take(value: u8[2]): none {} func main(): none { var number: u8 = 255 as u8; var value: u8[2] = { number }; var copy: u8[2] = value; take(copy); }' }
)
try {
    foreach ($case in $cases) {
        $projectDir = Join-Path $probeDir $case.Name
        $sourceDir = Join-Path $projectDir 'src'
        New-Item -ItemType Directory -Path $sourceDir | Out-Null
        Set-Content -LiteralPath (Join-Path $sourceDir 'main.sg') -Value $case.Code
        Push-Location $projectDir
        try { $output = & dotnet $compilerPath build 2>&1; $exitCode = $LASTEXITCODE } finally { Pop-Location }
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $projectDir 'obj/main.c'))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |Fatal') {
            throw "$($case.Name): expected successful native build.`n$text"
        }
    }
    Write-Output "Array cast checks passed: $($cases.Count) build-only cases; no invalid program is executed."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-array-casts-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
