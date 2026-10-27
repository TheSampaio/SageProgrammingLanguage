# Separate native compilation - 2026-10-26

Sage 0.36.1 compiles each generated C module with `gcc -c` into
`obj/<module>.o` (in parallel) and links the objects in module discovery order
with the `extern` link libraries. Flags, include directories, and exit-status
behavior match the previous single GCC command.

Debug and Release builds completed without errors. All 31 PowerShell scripts
passed in both configurations. Sandbox passed 26 files and 203 assertions in
each configuration. `tests/native-build.ps1` checks one object per module in a
four-module project, the linked executable's output and exit code (also through
`run`), forwarding of an `extern` link library (`ws2_32`), and a nonzero exit
with GCC's diagnostic when a native compile fails.
