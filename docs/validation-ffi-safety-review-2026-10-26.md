# FFI safety review and toolchain isolation - 2026-10-26

Sage 0.31.3 records a review of the C FFI safety requirements (buffer lengths,
encoding, nullability, allocation, destruction, ownership transfer, borrowed
native memory, callbacks, conversions, error propagation) in
[native boundaries](native-boundaries.md), classifying each as enforced, trusted,
or unsupported. The only compiler change is S213: `none` is rejected as an extern
parameter type instead of failing later in GCC (`none*` stays valid).
`tests/native-boundaries.ps1` gained four cases (S213, `none*`, borrow S173,
callback S003).

The Clang decision is documented in [C++ interop](cpp-interop.md): no `clang`
is installed here, automatic header parsing is deferred, and no mandatory
dependency is added. `tests/cpp-toolchain-isolation.ps1` puts a failing `g++`
shim first on `PATH`; three C-only programs (std module, a C block named `cpp`,
a declared C source) build without touching it, and an `extern cpp` control
program reaches it.
