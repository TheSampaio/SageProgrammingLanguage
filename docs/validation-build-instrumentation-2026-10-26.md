# Build instrumentation - 2026-10-26

Sage 0.36.3 adds development-only instrumentation behind the
`SAGE_INSTRUMENTATION` symbol (`dotnet build -c Instrumented` or
`-p:SageInstrumentation=true`): per-phase timings, cache hit/miss counts,
peak working set, managed allocation and GC counts, printed to standard error.
Recording methods are `[Conditional]`, so Debug and Release compile them away.
The `gcc -v` toolchain probe now starts in the background with the front end;
on the Sandbox arithmetic test the measured cache phase dropped from 46 ms to
about 12 ms on a no-change build (one run each, instrumented build).

Debug, Release and Instrumented builds completed without errors. All 33
PowerShell scripts passed in Debug and Release. Sandbox passed 26 files and 203
assertions in each configuration. `tests/instrumentation.ps1` checks that a
normal build prints no instrumentation and leaves only the expected `obj/` and
`bin/` artifacts, then builds the Instrumented configuration and checks every
metric plus the clean (2 compiled) and no-change (2 reused) cache counts.
