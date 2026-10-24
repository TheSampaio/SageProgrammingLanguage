# Fixed-array bounds checks (v0.9.4)

Every access to a fixed-size Sage array checks its declared bounds. An integer
literal index (including unary minus) outside `[0, length)` is rejected with
`S185` before C generation. Other indices are checked during native execution
before the indexed read, write, or increment.

```sage
var values: i32[2] = { 4, 5 };
var index: i32 = 2;
// values[2] would be an S185 compile error.
var value: i32 = values[index]; // runtime S186; process exits with status 1.
```

A dynamic failure writes `Sage runtime error S186:` to stderr with the declared
range and access coordinates, then terminates with `EXIT_FAILURE`. The CLI
propagates that status to callers, including the VS Code extension. This is a
process termination, not recoverable error handling; it does not unwind Sage
ownership cleanup on that failure path.

Generated code evaluates the array base first, then its index, once each. The
checked expression remains an lvalue for assignment and increment, including
arrays nested in struct fields. Const-qualified arrays retain read-only element
access. GCC statement expressions and `__auto_type` implement this lowering;
GCC was already required by the backend. Normal successful access adds no output.

Fixed-array lengths in Sage bindings, structs, and function contracts must be
positive and representable as `i32`; invalid lengths are `S187`. Constant folding
of arithmetic, named constants, and casts is not implemented here; their indices
receive the runtime check instead.

Arrays remain checked inside `unsafe` blocks. Raw pointer indexing remains an
unchecked native operation requiring `unsafe`. A native pointer's real allocation
size, nullability, native ABI declarations, array argument aliasing, and ownership
are separate contracts. In particular, checked array parameters trust the declared
length: this does not validate memory supplied through an unsafe/native boundary.

Validate semantic and native failures with `tests/array-bounds.ps1`; the Sandbox
`array_bounds.test.sg` covers successful access and single evaluation of side
effects. These checks do not establish complete memory safety for Sage.
