# Sage 0.20.0 bounded byte-path validation

Validated on Windows, 2026-10-25:

- Debug and Release builds succeeded. All 21 PowerShell regression scripts
  passed in both configurations; Sandbox passed 23 files / 157 assertions each.
- `tests/array-casts.ps1`: ten build-only cases. A scalar-to-array argument was
  accepted in 0.19.0 and generated `take(((u8)0))` against a 260-byte copying
  parameter. It is now rejected with S188 before C generation. No invalid native
  program was executed. Pointer/array-to-array, array-to-scalar/text casts are
  also rejected; normal copies/scalar casts and explicit unsafe array-to-pointer
  interop retain their contracts.
- Native byte-file fixture: 66 assertions with `-std=c11 -Wall -Wextra -Werror`.
  Added prefix validation for null, zero/oversized lengths, embedded zero,
  controls/non-ASCII, no open/allocation on invalid prefixes, preservation of
  existing data, input without termination, ignored trailing bytes, allocation
  failure cleanup, and native termination of a 259-byte prefix. The maximum-length
  case injects open failure so it does not depend on OS filename limits.
- Sage integration: 23 assertions, all 256 byte values, persisted contents,
  safe ASCII path creation, invalid prefix/length, and exclusive reopen after
  explicit and implicit close. Twelve negative cases retain ownership/borrow
  errors and reject mismatched or fabricated path arrays before C generation.
- Runtime relocation tests keep all native support outside the Sage-only std.
- VS Code with the installed/reloaded companion extension 0.8.0, in the existing
  Sandbox workspace: Test Explorer passed 180/180 (23 file results + 157 asserts).
  Run Current File created a safe byte-path file containing byte 42; independent
  exclusive reopen confirmed close. The same command rejected a fabricated array
  with S188 at `(1,63)` and exit 1. Task-created probes were closed and removed.
- Extension: JavaScript syntax check, 17 Node tests, VSIX packaging (13 files),
  installation and reload succeeded. Material Icon Theme remains selected;
  the extension contributes a language icon without an icon-theme replacement.

The safe constructor copies a fixed `u8[260]` value parameter and validates a
1..259-byte printable ASCII prefix before opening/truncating. It returns an empty
owner on failure. This does not provide general owned strings, Unicode paths,
OS path validity guarantees, or complete language memory safety. Legacy
`create(str)` remains unsafe. Native open/allocation failures may already have
truncated an existing file; validation rejection is the no-open guarantee.
