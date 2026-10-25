# Explicit native boundaries (v0.11.0)

Every function declared by `extern` has an implicitly unsafe callable contract.
Calling it requires a lexical `unsafe { ... }` block or an `unsafe func`; otherwise
the compiler reports `S164` before C generation. The rule applies to scalar-only
bindings as well as pointer-bearing APIs: a declaration is not a safety proof.

```sage
extern native("stdlib.h") { func llabs(value: c_longlong): c_longlong; }

// Every i32 value and its magnitude fit in c_longlong.
func magnitude(value: i32): i64
{
    unsafe { return native::llabs(value as c_longlong) as i64; }
}
```

A Sage wrapper may expose a regular function when its implementation establishes
the required contract. Its native operations remain explicit inside local unsafe
blocks. A caller's unsafe context does not authorize native calls in the separately
analyzed body of a regular wrapper. Returning from an unsafe block and nested
scope restoration follow the same function/ownership rules as ordinary blocks.

This is a deliberate migration from earlier releases, which allowed direct native
calls from ordinary functions. Add the local boundary where the native operation
occurs, or mark the entire API `unsafe func` when callers must uphold requirements
the wrapper cannot establish. Tests using native instrumentation also state that
boundary explicitly; ownership, borrowing and bounds checks remain enabled there.

## Standard library migration

- `console.print`, `print_line`, `clear`, `read`, and `run` retain their public
  signatures. Native calls are isolated in explicit implementation blocks.
  Output uses fixed format strings. `run` intentionally executes its argument
  through a command shell; it is not an argument-escaping API.
- The legacy `console.read_line(buffer: str)` is now `unsafe func`. It uses an
  unbounded token read and has no capacity contract. A string literal is not a
  writable buffer. A bounded, ownership-aware replacement remains future work.
- Legacy `http.fetch`, `post`, and `download` are now `unsafe func`: they interpolate
  caller data into shell commands without escaping it. This increment does not
  harden that transport. `get_into`, file, memory, and server raw interfaces already
  require unsafe callers.
- String assertions isolate native `strcmp` internally. Their legacy `str`
  precondition still requires valid, terminated strings; string ownership and
  lifetime validation are not added by a surrounding unsafe block.

## Guarantees and limits

The compiler checks the unsafe call boundary, declared arity, and repeated native
alias consistency. It still trusts the declared ABI and native argument conversions.
It does not infer buffer capacity, nullability, allocation/destruction pairs,
callback lifetime, encoding, or resource ownership from a C function's name.
Native alias visibility/encapsulation is also separate from this call-context rule.

An unsafe block is an explicit trust boundary, not a proof that the enclosed
operation is safe. Existing checked Sage ownership and borrowing rules still
reject invalid moves, anonymous allocations, conflicting loans, and fixed-array
accesses inside it. General native pointer operations remain unchecked.

Validate with `tests/native-boundaries.ps1` and
`Sandbox/tests/native_boundaries.test.sg`. HTTP and unbounded input helpers are
compiled in unused test functions; the suite performs no HTTP request or input read.
