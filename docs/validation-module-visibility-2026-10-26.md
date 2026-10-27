# Module visibility and C symbol collisions - 2026-10-26

Sage 0.34.0 resolves Sage callables only in their declaring file and in files
that directly `use` that file. Transitively discovered modules and top-level
functions report `S153` with the file to import. Native extern aliases are
private to their declaring files; unsafe gating (`S164`) is still reported
first for safe callers. Distinct callables that lower to one C symbol (`a_b::c`
versus `a::b_c`, `alpha_value` versus `alpha::value`, module `main`, or a Sage
function named like a declared native function) report `S155`.

Migration: `Sandbox/tests/byte_file_buffer.test.sg`, two fixtures and the
`tests/byte-file-buffer.ps1` negative-case prefix now import `byte_buffer`
directly instead of relying on `byte_file`'s import.

`tests/module-visibility.ps1` covers 12 cases: direct module and top-level
imports at runtime, transitive module/top-level/interpolated calls, imported
native aliases, same-file modules, four C-name collision shapes, a cross-file
collision, and native aliases sharing one C function.

Debug and Release builds completed with zero warnings/errors. All 30
PowerShell scripts passed in both configurations; Sandbox passed 26 files and
203 assertions in each.
