param(
    [Parameter(Mandatory = $true)]
    [string]$CompilerDll
)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ("sage-methods-" + [guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $probeDir 'src'
New-Item -ItemType Directory -Path $sourceDir | Out-Null

$io = 'extern io("stdio.h") { func puts(text: str): c_int; } '
$check = 'unsafe { if (ok) { io::puts("[PASS] methods"); } else { io::puts("[FAIL] methods"); } }'
$runtime = 'use counter; use heap; ' + $io + 'func main(): none { ' +
    'var c: Counter = { count = 1, limit = 9 }; c.bump(4); var ok: b8 = c.read() == 5 && counter::read(&c) == c.read(); ' +
    'ok = ok && c.count == 5 && c.count() == 50; ' +
    'var d: i32 = c.read().doubled(); ok = ok && d == 10 && counter::doubled(d) == 20; ' +
    'c.twice(); ok = ok && c.read() == 7; ' +
    'const fixed: Counter = { count = 3, limit = 0 }; ok = ok && fixed.read() == 3; ' +
    'var text: str = "{c.read()}"; ' +
    'var block: Block = heap::make(); ok = ok && block.present(); ' +
    'var copy: Block = block.duplicate(); ok = ok && copy.present(); ' +
    'c.absorb(copy); ok = ok && block.present(); ' +
    'if (true) { var view: &Counter = &c; ok = ok && view.read() == 7; } c.bump(1); ok = ok && c.read() == 8; ' +
    $check + ' }'

$cases = @(
    @{ Name = 'runtime'; Code = $runtime; Pass = $true },
    @{ Name = 'qualified_equivalent'; Code = 'use counter; ' + $io + 'func main(): none { var c: Counter = { count = 2, limit = 0 }; counter::bump(&mut c, 3); var ok: b8 = c.read() == counter::read(&c) && c.read() == 5; ' + $check + ' }'; Pass = $true },
    @{ Name = 'own_import_only'; Code = 'use counter; ' + $io + 'func main(): none { var x: i32 = 4; var ok: b8 = x.doubled() == 8; ' + $check + ' }'; Pass = $true },
    @{ Name = 'ambiguous_imports'; Code = 'use counter; use other; func main(): none { var x: i32 = 4; var y: i32 = x.doubled(); }'; Error = 'S158' },
    @{ Name = 'ambiguous_local_module'; Code = 'use counter; module local { method func doubled(value: i32): i32 { return value; } } func main(): none { var x: i32 = 4; var y: i32 = x.doubled(); }'; Error = 'S158' },
    @{ Name = 'not_opted_in'; Code = 'use counter; func main(): none { var c: Counter = { count = 1, limit = 0 }; var y: i32 = c.plain(); }'; Error = 'S159' },
    @{ Name = 'unimported_module'; Code = 'use counter; func main(): none { var x: i32 = 4; var y: i32 = x.tripled(); }'; Error = 'S153' },
    @{ Name = 'missing_method'; Code = 'use counter; func main(): none { var x: i32 = 4; var y: i32 = x.missing(); }'; Error = 'S156' },
    @{ Name = 'exact_receiver_type'; Code = 'use counter; func main(): none { var x: i64 = 4; var y: i64 = x.doubled(); }'; Error = 'S156' },
    @{ Name = 'field_not_callable'; Code = 'use counter; func main(): none { var c: Counter = { count = 1, limit = 0 }; var y: i32 = c.limit(); }'; Error = 'S156' },
    @{ Name = 'borrow_conflict_receiver'; Code = 'use counter; func main(): none { var c: Counter = { count = 1, limit = 0 }; var view: &Counter = &c; c.bump(1); }'; Error = 'S178' },
    @{ Name = 'same_call_conflict'; Code = 'use counter; func main(): none { var c: Counter = { count = 1, limit = 0 }; c.merge(&c); }'; Error = 'S172' },
    @{ Name = 'const_mutable_receiver'; Code = 'use counter; func main(): none { const c: Counter = { count = 1, limit = 0 }; c.bump(1); }'; Error = 'S171' },
    @{ Name = 'shared_reborrow_mutable'; Code = 'use counter; module local { method func poke(value: &Counter): none { value.bump(1); } } func main(): none {}'; Error = 'S171' },
    @{ Name = 'temporary_borrow_receiver'; Code = 'use counter; struct Pair { inner: Counter; } func main(): none { var p: Pair = { inner = { count = 1, limit = 0 } }; var y: i32 = p.inner.read(); }'; Error = 'S175' },
    @{ Name = 'use_after_move_argument'; Code = 'use counter; use heap; func main(): none { var c: Counter = { count = 1, limit = 0 }; var block: Block = heap::make(); c.absorb(block); var y: b8 = block.present(); }'; Error = 'S140' },
    @{ Name = 'moved_receiver'; Code = 'use heap; func main(): none { var block: Block = heap::make(); heap::consume(block); var y: b8 = block.present(); }'; Error = 'S140' },
    @{ Name = 'consuming_receiver'; Code = 'use heap; module local { method func close(block: Block): none {} } func main(): none {}'; Error = 'S157' },
    @{ Name = 'receiverless_method'; Code = 'module local { method func make(): i32 { return 1; } } func main(): none {}'; Error = 'S157' },
    @{ Name = 'pointer_receiver'; Code = 'module local { unsafe method func raw(value: i32*): none {} } func main(): none {}'; Error = 'S157' },
    @{ Name = 'top_level_method'; Code = 'method func doubled(value: i32): i32 { return value; } func main(): none {}'; Error = 'S157' },
    @{ Name = 'unsafe_method_call'; Code = 'module local { unsafe method func raw(value: i32): i32 { return value; } } func main(): none { var x: i32 = 1; var y: i32 = x.raw(); }'; Error = 'S164' },
    @{ Name = 'unsafe_method_context'; Code = 'module local { unsafe method func raw(value: i32): i32 { return value; } } ' + $io + 'func main(): none { var x: i32 = 1; unsafe { var ok: b8 = x.raw() == 1; ' + $check + ' } }'; Pass = $true },
    @{ Name = 'method_arity'; Code = 'use counter; func main(): none { var c: Counter = { count = 1, limit = 0 }; c.bump(); }'; Error = 'S160' },
    @{ Name = 'resource_result_requires_owner'; Code = 'use heap; func main(): none { var block: Block = heap::make(); var ok: b8 = block.duplicate().present(); }'; Error = 'S193' }
)

try {
    Set-Content -LiteralPath (Join-Path $sourceDir 'counter.sg') -Value @'
use other;
use heap;
struct Counter { count: i32; limit: i32; }
module counter
{
    method func read(value: &Counter): i32 { return value.count; }
    method func count(value: &Counter): i32 { return value.count * 10; }
    method func bump(value: &mut Counter, amount: i32): none { value.count = value.count + amount; }
    method func twice(value: &mut Counter): none { value.bump(1); value.bump(1); }
    method func merge(target: &mut Counter, source: &Counter): none { target.count = target.count + source.count; }
    method func doubled(value: i32): i32 { return value * 2; }
    method func absorb(target: &Counter, block: Block): none {}
    func plain(value: &Counter): i32 { return value.count; }
}
'@
    Set-Content -LiteralPath (Join-Path $sourceDir 'other.sg') -Value @'
module other
{
    method func doubled(value: i32): i32 { return value * 3; }
    method func tripled(value: i32): i32 { return value * 3; }
}
'@
    Set-Content -LiteralPath (Join-Path $sourceDir 'heap.sg') -Value @'
extern native_heap("stdlib.h")
{
    func malloc(size: c_size_t): c_void*;
    func free(value: c_void*): none;
    resource Block: c_void* drop free;
}
module heap
{
    func make(): Block { unsafe { var block: Block = native_heap::malloc(8 as c_size_t) as Block; return block; } }
    method func present(block: &Block): b8 { return block != null; }
    method func duplicate(block: &Block): Block { var fresh: Block = make(); return fresh; }
    func consume(block: Block): none {}
}
'@
    $source = Join-Path $sourceDir 'main.sg'
    foreach ($case in $cases) {
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        if ($case.Error) {
            if ($exitCode -eq 0 -or $text -notmatch ("error " + $case.Error + ':')) {
                throw "$($case.Name): expected $($case.Error), got status $exitCode.`n$text"
            }
        } elseif ($exitCode -ne 0 -or $text -notmatch '\[PASS\] methods' -or $text -match '\[FAIL\]') {
            throw "$($case.Name): expected a successful run.`n$text"
        }
    }
    Write-Output "Extension method checks passed: $($cases.Count) cases."
}
finally {
    Remove-Item -LiteralPath $probeDir -Recurse -Force
}
