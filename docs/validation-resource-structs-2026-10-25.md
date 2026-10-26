# Local resource structs — 2026-10-25

Sage 0.25.0 adds direct native resource fields to local structs. Named owners
move into literal fields; generated C cleanup releases fields in reverse order,
including on early return. Empty/omitted fields are null and skipped.

Debug and Release builds completed with zero warnings/errors. All 26 PowerShell
scripts passed in both configurations. `tests/native-resource-struct.ps1`
checks ten native runtime assertions, 18 compiler cases, and two import cases.
Sandbox passed 26 files and 203 assertions in each configuration. It rejects copies, field replacement,
anonymous sources, borrowed sources, live-loan conflicts, arrays, nested
resource structs, globals, calls, returns, and `new`. The companion VS Code
extension 0.12.0 passed 17 Node tests, JavaScript syntax check, and VSIX
packaging; its manifest test confirms no replacement file icon theme.

This does not complete Phase 2. See [the exact supported subset](resource-structs.md).
