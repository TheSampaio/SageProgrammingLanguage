# Sage 0.33.2 Text file paths validation

Validated on Windows, 2026-10-26:

- Debug and Release builds: 41 PowerShell scripts passed; Sandbox passed
  30 files / 291 assertions in each configuration.
- `Sandbox/tests/byte_file_text.test.sg` (11 assertions): create from a `Text`
  path, write all bytes, consuming close, read-only reopen, read back into a
  `Text` with EOF, read-only write rejection, capacity-bounded reads, and
  rejected missing/control-character/empty paths.
- `tests/byte-file.ps1` adds two rejected programs: a `Text` path without a
  borrow (S170) and `read_text` into a shared `&Text` (S171).
- Existing byte-file/buffer/read-only scripts, including their stream and
  allocation trackers, pass unchanged apart from copying `text.sg`, which
  `byte_file` now imports.

`file.sg` and `memory.sg` changed only in documentation comments. No compiler
code changed; the increment adds `Runtime/include/sage_byte_file_text.h`.
