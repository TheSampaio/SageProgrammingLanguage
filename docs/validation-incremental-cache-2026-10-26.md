# Incremental native cache - 2026-10-26

Sage 0.36.2 reuses native objects and executables from a content-addressed
cache (`obj/sage-build-cache.json`). Keys cover the generated C, every header it
includes transitively, `SageInfo.Version`, compiler/flags, and the `gcc -v`
identity. `--clean` forces a full rebuild.

Debug and Release builds completed without errors. All 32 PowerShell scripts
passed in both configurations. Sandbox passed 26 files and 203 assertions in
each configuration, sharing one `Sandbox/obj` cache between the Debug and
Release compilers. `tests/incremental-build.ps1` checks, on a five-module
project and against a `--clean` rebuild after each step: no-change rebuilds
compile and link nothing and keep object timestamps; a body-only change
recompiles one unit; interface changes recompile exactly the module and its
dependents; missing/tampered objects and a missing executable are rebuilt; a
corrupted manifest triggers a full rebuild; a relocated compiler reuses every
object; a runtime header edit recompiles exactly its includers; and a different
compiler version recompiles every unit. Normal builds print no cache output.
