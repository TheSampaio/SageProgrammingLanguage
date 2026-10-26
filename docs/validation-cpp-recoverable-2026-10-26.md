# Recoverable C++ exceptions - 2026-10-26

Sage 0.31.1 lets an `extern cpp` binding opt in to recoverable failures with a
trailing `catch`. The adapter contains the exception, returns a zero result, and
the block's `= catch` (`b8`) and `= catch.message` (`str`) bindings report the
latest failure on the calling thread. Abort remains the default; adapters
without `catch` bindings carry no failure state. See [the contract](cpp-interop.md).

`tests/cpp-recoverable.ps1` runs a program covering success, `std::exception`,
non-standard throws, a throwing constructor (null handle, one drop only for the
live object), a throwing member, and a null-handle member call, then checks that
bindings without `catch` still abort and carry no failure state. Ten rejection
cases (S210 failure-query shapes and injection, S164, S152 recoverable/abort
conflict, S001 for unknown query or `catch` in a C block) report before code
generation.
