# Nested resource field replacement - 2026-10-26

Sage 0.30.0 extends standalone resource field replacement to paths of local
resource structs, such as `deep.outer.nested.first = replacement`. The source
must be a named local owner. Generated C clears the source, releases the old
field once, and installs the replacement. Recursive struct cleanup releases the
new field on scope exit or early return. Field paths through array elements
remain unsupported.

Debug and Release builds completed with zero warnings/errors. All 29 PowerShell
scripts passed in both configurations; Sandbox passed 26 files and 203
assertions in each. `tests/native-resource-nested-struct.ps1` checked 19 native
runtime assertions and 24 compiler cases. It covers destruction order, empty
old fields, early return, moved and borrowed sources, live loans, constant
roots, nested expression use, and loop transfer restrictions. The resource
struct array test rejects replacement through both direct and nested fields of
array elements.

The companion VS Code extension 0.17.0 passed 17 Node tests, JavaScript syntax
check, and VSIX packaging. Its manifest test confirms it does not replace the
active file icon theme.
