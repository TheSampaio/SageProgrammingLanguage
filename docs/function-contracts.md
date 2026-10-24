# Function contracts (v0.8.5)

Callable metadata now includes immutable parameter types, return type, unsafe
status, and native origin when applicable. Semantic analysis checks argument
count before generating C. Sage function arguments must match their declared
types using the same compatibility and numeric-literal rules as assignments.
Return expressions must match the declared result, and a non-`none` function
cannot fall through without returning. Literal infinite loops have no fallthrough
in the current language, which has no implemented `break` statement.

Struct values can be passed and returned by value. A struct initializer receives
the expected type from its parameter or return context. These contracts do not
allow transferring owning heap references through value parameters; `S144`
remains in force until explicit reference contracts are implemented.

Calling an `unsafe func` requires an `unsafe` block or another unsafe function.
Existing direct `extern` calls retain their current trusted ABI behavior during
migration. Their declared arity is checked, but argument type conversions and
the real C header's ABI are not yet validated by Sage.

Repeated native aliases are accepted only when return type, ordered parameter
types, unsafe status, header, and linked library agree. Parameter names do not
change the ABI. Conflicting declarations fail before native compilation. This
does not prove that a declaration matches the actual C library.

| Code | Meaning |
|------|---------|
| S152 | Repeated native alias has inconsistent contract or origin |
| S154 | Duplicate parameter name |
| S160 | Wrong argument count |
| S161 | Wrong Sage argument type |
| S162 | Wrong return type or return outside a function |
| S163 | Non-`none` function can reach its end without a result |
| S164 | Unsafe Sage call outside an unsafe context |

C interop aliases remain distinct from fixed-width Sage types. The console
wrappers now explicitly cast their native `c_int` results to `i32`, following
the existing FFI convention. Run `tests/function-contracts.ps1` plus the Sandbox
suite to validate the contracts and their runtime behavior.
