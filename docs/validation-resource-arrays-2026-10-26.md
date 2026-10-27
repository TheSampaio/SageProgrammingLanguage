# Local native resource arrays — 2026-10-26

Sage 0.27.0 supports fixed local arrays of direct native resource owners.
Literal elements transfer named owners, missing slots are null, and generated
cleanup visits elements in reverse index order. No resource-array copy or
element extraction contract is implied.

Debug and Release builds completed with zero warnings/errors. All 28 PowerShell
scripts passed in both configurations; Sandbox passed 26 files and 203
assertions in each configuration. `tests/native-resource-array.ps1` checks ten native runtime assertions and
19 compiler cases. The native tracker checks liveness,
destruction order, partial initialization, early return, loop iterations,
const owners, and empty arrays.
Negative cases cover duplication, borrowed sources, live loans, copies,
assignment, element replacement/consumption, global and signature use.

The companion VS Code extension 0.14.0 passed 17 Node tests, JavaScript syntax
check, and VSIX packaging. Its manifest test confirms no replacement file
icon theme.
