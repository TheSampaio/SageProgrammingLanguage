# Direct resource field replacement - 2026-10-26

Sage 0.29.0 permits a standalone assignment from a named local native resource
owner to a direct resource field of a mutable local resource struct. Generated C
clears the source, releases the old field, and installs the replacement. The
struct's existing cleanup releases the new field at scope exit. Empty old fields
skip destruction. Nested fields and array element fields remain unsupported.

Debug and Release builds completed with zero warnings/errors. All 29 PowerShell
scripts passed in both configurations; Sandbox passed 26 files and 203
assertions in each. `tests/native-resource-struct.ps1` checked 19 native runtime
assertions, 25 compiler cases, and two import cases. The new cases cover source
invalidation, old/new destructor counts, an empty destination, borrowed or
moved sources, conflicting loans, a constant destination, and expression use.
Nested and array element field replacement are rejected in their respective
test scripts.

The companion VS Code extension 0.16.0 passed 17 Node tests, JavaScript syntax
check, and VSIX packaging. Its manifest test confirms it does not replace the
active file icon theme.
