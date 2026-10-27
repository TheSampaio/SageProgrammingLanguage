# Nested local resource structs — 2026-10-26

Sage 0.26.0 supports nested local struct literals containing native resource
fields. The inner type must be declared earlier in the same Sage source.
Generated cleanup recurses through fields in reverse declaration order and
skips omitted/null handles.

Debug and Release builds completed with zero warnings/errors. All 27 PowerShell
scripts passed in both configurations. Sandbox passed 26 files and 203
assertions in each configuration. `tests/native-resource-nested-struct.ps1`
checks 10 native runtime assertions and 16 compiler cases. The native tracker checks cleanup
order across three levels, early return, partial initialization, and empty
structs. Negative cases cover duplicated owners, borrowed sources, struct
copies, arrays, pointers, forward inner types, and owning signatures.

The companion VS Code extension 0.13.0 passed 17 Node tests, JavaScript syntax
check, and VSIX packaging. Its manifest test confirms that it does not register
a replacement file icon theme.
This increment does not add whole-struct moves, resource arrays, or general
memory safety for legacy strings and unsafe native code.
