param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-resource-parameters-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$contract = 'extern native("stdio.h") { type FILE; func fclose(value: FILE*): c_int; resource OwnedFile: FILE* drop fclose; } '
$factory = 'func make(): OwnedFile { unsafe { var owner: OwnedFile = (0 as FILE*) as OwnedFile; return owner; } } '
$consumer = 'func consume(value: OwnedFile): none {} '
$owner = 'var owner: OwnedFile = make(); '
$cases = @(
    @{ Name = 'consume_named_owner'; Code = $consumer + 'func main(): none { ' + $owner + 'consume(owner); }' },
    @{ Name = 'consume_const_owner'; Code = $consumer + 'func main(): none { const owner: OwnedFile = make(); consume(owner); }' },
    @{ Name = 'identity_return'; Code = 'func identity(value: OwnedFile): OwnedFile { return value; } func main(): none { ' + $owner + 'var result: OwnedFile = identity(owner); }' },
    @{ Name = 'parameter_local_move'; Code = 'func consume(value: OwnedFile): none { var local: OwnedFile = value; } func main(): none { ' + $owner + 'consume(owner); }' },
    @{ Name = 'parameter_borrow'; Code = 'func observe(value: &OwnedFile): none {} func consume(value: OwnedFile): none { observe(&value); } func main(): none { ' + $owner + 'consume(owner); }' },
    @{ Name = 'distinct_owned_and_borrowed'; Code = 'func mix(a: OwnedFile, b: &OwnedFile): none {} func main(): none { ' + $owner + 'var other: OwnedFile = make(); mix(owner, &other); var moved: OwnedFile = other; }' },
    @{ Name = 'distinct_borrowed_and_owned'; Code = 'func mix(a: &OwnedFile, b: OwnedFile): none {} func main(): none { ' + $owner + 'var other: OwnedFile = make(); mix(&other, owner); }' },
    @{ Name = 'two_owned_arguments'; Code = 'func consume(a: OwnedFile, b: OwnedFile): none {} func main(): none { ' + $owner + 'var other: OwnedFile = make(); consume(owner, other); }' },
    @{ Name = 'stable_scalar_argument'; Code = 'func consume(value: OwnedFile, n: i32): none {} func main(): none { ' + $owner + 'var n: i32 = 1; consume(owner, n + 1); }' },
    @{ Name = 'imported_consumer'; Code = 'use resource_consumer; func main(): none { ' + $owner + 'consume(owner); }'; NoPrefix = $true },
    @{ Name = 'forward_parameter_type'; Code = $consumer + $contract + $factory + 'func main(): none { ' + $owner + 'consume(owner); }'; NoPrefix = $true },
    @{ Name = 'use_after_call'; Code = $consumer + 'func main(): none { ' + $owner + 'consume(owner); owner; }'; Error = 'S140' },
    @{ Name = 'duplicate_owned_argument'; Code = 'func both(a: OwnedFile, b: OwnedFile): none {} func main(): none { ' + $owner + 'both(owner, owner); }'; Error = 'S140' },
    @{ Name = 'borrow_after_owned_argument'; Code = 'func mix(a: OwnedFile, b: &OwnedFile): none {} func main(): none { ' + $owner + 'mix(owner, &owner); }'; Error = 'S194' },
    @{ Name = 'owned_after_borrow_argument'; Code = 'func mix(a: &OwnedFile, b: OwnedFile): none {} func main(): none { ' + $owner + 'mix(&owner, owner); }'; Error = 'S194' },
    @{ Name = 'move_through_borrow'; Code = $consumer + 'func steal(value: &OwnedFile): none { consume(value); } func main(): none {}'; Error = 'S191' },
    @{ Name = 'consume_with_live_loan'; Code = $consumer + 'func main(): none { ' + $owner + 'var view: &OwnedFile = &owner; consume(owner); }'; Error = 'S178' },
    @{ Name = 'borrow_from_consumed'; Code = $consumer + 'func observe(value: &OwnedFile): none {} func main(): none { ' + $owner + 'consume(owner); observe(&owner); }'; Error = 'S140' },
    @{ Name = 'anonymous_owned_argument'; Code = $consumer + 'func main(): none { consume(make()); }'; Error = 'S194' },
    @{ Name = 'anonymous_adoption_argument'; Code = $consumer + 'unsafe func main(): none { consume((0 as FILE*) as OwnedFile); }'; Error = 'S194' },
    @{ Name = 'wrong_scalar_argument'; Code = $consumer + 'func main(): none { consume(1); }'; Error = 'S194' },
    @{ Name = 'effectful_scalar_argument'; Code = 'func zero(): i32 { return 0; } func consume(value: OwnedFile, n: i32): none {} func main(): none { ' + $owner + 'consume(owner, zero()); }'; Error = 'S194' },
    @{ Name = 'effectful_argument_before_move'; Code = 'func zero(): i32 { return 0; } func consume(n: i32, value: OwnedFile): none {} func main(): none { ' + $owner + 'consume(zero(), owner); }'; Error = 'S194' },
    @{ Name = 'consumed_parameter_reuse'; Code = $consumer + 'func outer(value: OwnedFile): none { consume(value); value; } func main(): none {}'; Error = 'S140' },
    @{ Name = 'consumed_parameter_loop'; Code = $consumer + 'func outer(value: OwnedFile): none { while (true) { consume(value); } } func main(): none {}'; Error = 'S145' },
    @{ Name = 'entry_point_parameter'; Code = 'func main(value: OwnedFile): none {}'; Error = 'S191' },
    @{ Name = 'entry_point_resource_borrow'; Code = 'func main(value: &OwnedFile): none {}'; Error = 'S191' },
    @{ Name = 'native_owned_parameter'; Code = 'extern other("stdio.h") { func invalid(value: OwnedFile): none; } func main(): none {}'; Error = 'S191' },
    @{ Name = 'conditional_parameter_move'; Code = $consumer + 'func outer(value: OwnedFile, flag: b8): none { if (flag) { consume(value); } value; } func main(): none {}'; Error = 'S142' },
    @{ Name = 'returning_branch_preserves_parameter'; Code = $consumer + 'func outer(value: OwnedFile, flag: b8): OwnedFile { if (flag) { consume(value); var next: OwnedFile = make(); return next; } return value; } func main(): none { ' + $owner + 'var next: OwnedFile = outer(owner, false); }' },
    @{ Name = 'increment_argument'; Code = 'func consume(value: OwnedFile, n: i32): none {} func main(): none { ' + $owner + 'var n: i32 = 0; consume(owner, n++); }'; Error = 'S194' },
    @{ Name = 'module_consumer'; Code = 'module files { ' + $consumer + ' } func main(): none { ' + $owner + 'files::consume(owner); }' }
)

try {
    $minimalHeader = Join-Path $probeDir 'minimal-resource.h'
    Set-Content -LiteralPath $minimalHeader -Value 'typedef struct MinimalHandle MinimalHandle; static void minimal_drop(MinimalHandle* value) { (void)value; }'
    $minimalCode = 'extern minimal("' + $minimalHeader.Replace('\', '/') + '") { type MinimalHandle; func minimal_drop(value: MinimalHandle*): none; resource MinimalOwner: MinimalHandle* drop minimal_drop; } func consume(value: MinimalOwner): none {} func main(): none {}'
    Set-Content -LiteralPath (Join-Path $probeDir 'minimal.sg') -Value $minimalCode
    $output = & dotnet $compilerPath (Join-Path $probeDir 'minimal.sg') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Owned parameter cleanup must provide NULL without a resource local.`n$($output -join "`n")" }
    Set-Content -LiteralPath (Join-Path $probeDir 'resource_consumer.sg') -Value ($contract + $factory + $consumer)
    foreach ($case in $cases) {
        $source = Join-Path $probeDir ($case.Name + '.sg')
        $prefix = if ($case.NoPrefix) { '' } else { $contract + $factory }
        Set-Content -LiteralPath $source -Value ($prefix + $case.Code)
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -ne 1 -or $text -notmatch ("error " + $case.Error + ':') -or
                (Test-Path -LiteralPath (Join-Path $probeDir "obj/$($case.Name).c"))) {
                throw "$($case.Name): expected semantic $($case.Error) before C generation, got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -match 'error |\[FAIL\]') {
            throw "$($case.Name): expected successful execution.`n$text"
        }
    }
    Write-Output "Native resource parameter checks passed: $($cases.Count) cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    $cleanupPrefix = Join-Path ([IO.Path]::GetTempPath()) 'sage-resource-parameters-'
    if (!$cleanupPath.StartsWith($cleanupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside the resource-parameter test directory: $cleanupPath"
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
