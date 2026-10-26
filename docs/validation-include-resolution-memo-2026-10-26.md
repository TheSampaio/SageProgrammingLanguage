# Include resolution memo - 2026-10-26

Sage 0.36.4 memoizes include resolution in the build cache's include-closure
hasher, keyed by include search path, including directory and directive. Cache
keys are unchanged; only repeated file-system probes are avoided. On a 41-module
synthetic project (instrumented Release-optimized build, one run each) the
`cache` phase of a no-change rebuild dropped from about 180 ms to about 50 ms.

Debug and Release builds completed without errors. All 33 PowerShell scripts
passed in both configurations, including `tests/incremental-build.ps1`.
Sandbox passed 26 files and 203 assertions in each configuration.
