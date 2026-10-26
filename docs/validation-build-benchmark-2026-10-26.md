# Incremental build benchmark - 2026-10-26

`tools/bench-incremental.ps1` measures clean, no-change, body-change and
interface-change builds on a generated multi-module project and records the
compiler, toolchain, hardware, workload and iteration count. Measured results
for v0.30.0 (single GCC command) and v0.36.4 (separate compilation with the
incremental cache) are in [incremental builds](incremental-build.md#benchmarks):
on a 41-module project the median clean build went from 4098 ms to 2015 ms and
a no-change rebuild from 4141 ms to 665 ms. The precompiled-header
investigation (about 16 ms saved per compiled unit) did not justify an
implementation.

The script ran to completion against the v0.30.0 Release compiler and the
v0.36.4 Release and Instrumented compilers (5 iterations each, two workloads).
No compiler source changed in this increment; the v0.36.4 suite result (33
scripts, 26 Sandbox files, 203 assertions per configuration) applies.
