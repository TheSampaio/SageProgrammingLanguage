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
  writable buffer. Since v0.33.0 the bounded, ownership-aware replacement is
  `Text.read_line()` in the [`text` module](text.md).
- Legacy `http_client.fetch`, `post`, and `download` are now `unsafe func`: they interpolate
  caller data into shell commands without escaping it. This increment does not
  harden that transport. `get_into`, file, memory, and http_server raw interfaces already
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


## FFI safety review (v0.31.3)

Review of the native-interop safety requirements against the implementation.
Enforced means the compiler or generated code checks it; trusted means the
declared contract is believed and only the call boundary is checked; unsupported
means the construct is rejected or does not exist.

| Requirement | Enforced | Trusted | Unsupported |
| --- | --- | --- | --- |
| Buffer lengths | Fixed Sage arrays are bounds checked in Sage code and need positive lengths; `byte_buffer` and `byte_file` validate capacity and limits before I/O | Raw `T*` pointers and `c_size_t` lengths handed to C (`memcpy`, `read`) | Inferring a length from a pointer |
| String encoding | `byte_file` accepts 1..259 printable ASCII path bytes | `str` and `c_char*` are NUL-terminated bytes of unspecified encoding | Encoding validation or conversion at the boundary |
| Nullability | `owner == null` for resources and borrows; null handle member calls in C++ adapters (abort, or a recorded failure with `catch`); resource cleanup tolerates null | `0 as T*` in `unsafe`; extern results may be null | Non-null pointer types |
| Native allocation | Heap `new` ownership; `resource` adoption is explicit and unsafe | Whatever an extern returns is owned only if declared a `resource` | Inferring ownership from names such as `open` or `create` |
| Resource destruction | `resource ... drop` cleanup at scope exit, moves, and replacement; destructor ABI matched to the pointer | That the declared destructor really releases the handle | Double-pointer or unknown-layout handles |
| Ownership transfer | Moves, consuming parameters, and returns of named owners; use after move is rejected | Native code that stores or frees a pointer it was handed | Transfers through raw pointers |
| Borrowed native memory | `&` and `&mut` cannot cross the ABI (S173); borrows of resources are lexically checked | Raw pointers returned by C stay valid as long as C says | Borrowed returns and native lifetimes |
| Callback lifetimes | Function-typed parameters do not parse (S003) | | Callbacks and function pointers |
| Type conversions | `c_*` and Sage numeric types need explicit casts; C++ adapters accept only scalars, `str`, scalar pointers, and one handle pointer (S210); `none` parameters are rejected (S213) | Declared widths and signedness match the C prototype; adapter argument conversions follow C++ rules | Structs, arrays, references, and standard-library types in `extern cpp` |
| Error propagation | `byte_file` returns explicit outcomes; C++ exceptions never unwind into C (abort by default, `catch` for an explicit status) | `errno` and C return codes mean what the wrapper says | Automatic mapping of C error codes |
| Symbols and linkage | Declared symbol collisions (S212), missing native sources (S211), repeated alias consistency (S152) | Symbols defined in undeclared native sources or libraries | Collision detection inside native code |

Small check added with this review: `none` is rejected as an extern parameter
type (S213; it used to surface as a GCC error), and
`tests/native-boundaries.ps1` now pins the borrow and callback rejections.
Larger gaps (non-null pointer types, length-carrying slices for C buffers,
callbacks, error-code mapping) need language features and are deliberately not
approximated here.
