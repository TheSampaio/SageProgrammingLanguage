# Local resource struct arrays - 2026-10-26

Sage 0.28.0 supports local fixed arrays of same-file structs containing direct
or nested native resource fields. Direct element literals move named owners;
omitted fields and elements are zeroed. Generated C releases elements in reverse
index order and recursively releases fields in reverse declaration order.
Whole-array copies, individual resource element replacement/extraction, global
storage, parameters, returns, and borrowing remain unsupported.

Debug and Release builds completed with zero warnings/errors. All 29 PowerShell
scripts passed in both configurations. Sandbox passed 26 files and 203
assertions in each configuration. `tests/native-resource-struct-array.ps1`
checked 12 native runtime assertions and 20 compiler cases, including early
return, loop iteration, partial initialization, move invalidation, and rejected
copies and escapes.

The companion VS Code extension 0.15.0 passed 17 Node tests, JavaScript syntax
check, and VSIX packaging. Its manifest test confirms it does not replace the
active file icon theme.
