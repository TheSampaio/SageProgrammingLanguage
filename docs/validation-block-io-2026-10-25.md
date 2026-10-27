# Sage 0.22.0 bounded block-I/O validation

Validated on Windows, 2026-10-25:

- Debug and Release builds: zero warnings/errors. All 23 PowerShell scripts
  passed in both configurations; Sandbox passed 25 files / 198 assertions each.
- `tests/byte-file-buffer.ps1`: 65 assertions under C11 with
  `-Wall -Wextra -Werror`, 21 Sage integration assertions and nine rejected
  invalid programs before C generation.
- Native fault injection covers partial read/write counts, zero-progress errors,
  EOF with data and without data, simultaneous EOF/error indicators, unexpected
  short reads without indicators, full write counts with an error indicator,
  positioning failure, status reset, null/closed owners, invalid/extreme limits,
  no I/O on rejected/zero requests, unchanged shared sources, and initialized
  prefix replacement. Old target bytes remain hidden after short input/EOF.
- Native and Sage fixtures roundtrip all 256 byte values. PowerShell checks
  persisted data independently, including exactly the bytes accepted by injected
  short writes, and reopens the Sage file exclusively after consuming close.
- Nine negative cases retain exclusive file/target borrows, shared-source
  requirements, use-after-close/release checks, explicit limit conversion and
  the unsafe native-call boundary.
- Existing byte-file and byte-buffer regression suites also pass, including
  allocator instrumentation across generated translation units.
- Relocation tests and Release publish resolve the new
  `Runtime/include/sage_byte_file_buffer.h` with its dependencies. std contains
  only `.sg`; local byte-file distributions now also need `byte_buffer.sg`.
- VS Code Sandbox with extension 0.10.0 installed/reloaded: Run Current File
  successfully wrote bytes 0/255 and read count=2 with clean EOF. PowerShell
  independently verified persisted bytes and exclusive reopen after close. A
  shared-target probe reported S171 at `(1,109)` and exit 1. Probes were closed
  and removed. Test Explorer passed 223/223 (25 file results + 198 assertions).
- Extension syntax check, all 17 Node tests and VSIX packaging passed (13 files,
  53.01 KB). Material Icon Theme remains selected; no theme settings or replacement
  icon-theme contribution were added.

The API returns `ByteTransfer { count, eof, failed }`. Read replaces contents
after native input begins, exposing exactly count initialized bytes even on
failure; pre-transfer rejection/positioning failure preserves the target.
Write borrows an initialized prefix shared and preserves it. Zero limits with
valid owners are no-ops. Native status is per-file and captured while the
exclusive borrow is held. There is no rollback, implicit retry, durability
guarantee, or general thread-safety claim. Existing constructors still create/
truncate; safe general open modes and Unicode paths remain unfinished.
