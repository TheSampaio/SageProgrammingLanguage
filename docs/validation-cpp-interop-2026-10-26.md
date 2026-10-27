# Bounded C++ interop â€” 2026-10-26

Sage 0.30.0 adds `extern cpp` blocks with generated `extern "C"` adapters for
declared free functions, overloads, explicit template instances, and class
handles (constructor, destructor, member functions). Exceptions and null member
handles abort with a diagnostic instead of unwinding into C. Only builds that
declare `extern cpp` generate adapters and invoke `g++` (GCC 15.2.0 here).

Debug and Release builds completed with zero warnings/errors. All 30 PowerShell
scripts passed in both configurations; Sandbox passed 26 files and 203
assertions in each configuration. `tests/cpp-interop.ps1` checks six runtime
behaviors (C alias compatibility without adapters, overload/template/class
output, resource-driven destruction, single shared adapter across modules,
C headers free of C++ includes, exception and null-handle aborts) and 17
rejection cases before code generation.

This is the first Phase 3 C++ increment, not universal C++ compatibility.
Header parsing, references, standard-library types, recoverable exceptions,
and project C++ sources remain future work; see [the contract](cpp-interop.md).
