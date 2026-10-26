# Native resource replacement assignment — 2026-10-25

Sage 0.24.0 adds standalone replacement assignment between named local owners
of the same native resource type. The source is moved and invalidated. The
destination's prior handle is destroyed exactly once before it receives the
incoming handle. Empty destinations skip the destructor. Assignment to const,
self-assignment, active conflicting loans, borrowed sources, and anonymous
factory results are rejected before C generation.

Validation: Debug and Release builds completed with zero warnings/errors. All
25 PowerShell scripts passed in both configurations, including nine native
runtime assertions and nine negative compiler cases in
`tests/native-resource-reassignment.ps1`. The native tracker checks liveness,
cleanup order, and duplicate destruction. Sandbox passed 26 files and 203
assertions in each configuration. The companion VS Code extension passed 17 Node
tests, JavaScript syntax check, and VSIX packaging; its manifest test confirms
that it does not register a replacement icon theme.

This increment does not permit replacement of legacy `new` references or
ownership-bearing struct fields/arrays. Process termination still does not
unwind Sage scopes.
