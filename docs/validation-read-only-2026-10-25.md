# Sage 0.23.0 existing read-only file validation

Validated on Windows, 2026-10-25:

- Debug and Release builds: zero warnings/errors. All 24 PowerShell scripts
  passed; Sandbox passed 26 files / 203 assertions in each configuration.
- `tests/byte-file-read-only.ps1`: 44 native assertions under C11 with
  `-Wall -Wextra -Werror`, 19 Sage integration assertions and five rejected
  invalid programs before C generation. The strengthened writable-mode query
  checks were rerun explicitly in Debug and passed in the full Release run.
- Native instrumentation checks `rb` versus existing `w+b` construction,
  non-truncation, missing paths without creation, shared path validation before
  open, open/allocation failure cleanup, byte/block input and EOF, positioning
  and read failures, close failure without retry, and final allocation liveness.
- Forbidden output hooks count byte writes, block writes, flush and positioning.
  Read-only byte/block/zero-limit writes and flush reach none of them. Hooks
  deliberately avoid invoking incompatible stdio if a guard regresses, so the
  test does not execute undefined I/O to discover the failure.
- Sage reads a real Windows read-only fixture containing all 256 byte values.
  Contents and read-only attribute remain unchanged; missing files are absent.
  Existing create-mode owners still report writable and close successfully.
- A test-only adapter logs opens/closes across generated translation units:
  four opens and four matching closes, no duplicate/unknown close or live stream,
  including owning factory returns, early return and moved ownership. PowerShell
  also verifies exclusive reopen after close.
- Existing byte-file, buffer, block-I/O and generic ownership/borrow suites pass.
- Runtime relocation and Release publish include
  `Runtime/include/sage_byte_file_read_only.h`; std remains exclusively `.sg`.
- VS Code with installed/reloaded extension 0.11.0: Run Current File opened a
  real read-only fixture, read bytes 0/255 and rejected byte/block writes and
  flush. Persisted content, read-only attribute and exclusive reopen were checked
  independently. Query after consuming close reported S140 at `(1,123)`, move
  `(1,77)`, exit 1. Probes were closed/removed; Test Explorer passed 229/229
  (26 file results + 203 assertions).
- Extension JavaScript check, 17 Node tests and VSIX packaging passed (13 files,
  53.48 KB). Material Icon Theme remains selected, with no replacement theme or
  theme-setting change.

`open_read_bytes` retains the bounded printable ASCII path contract. Read-only
capability belongs to the acquired native handle and survives moves/returns;
restrictions are runtime checks on `ByteFile`, not separate Sage types.
`can_write` reports mode permission, not OS permissions or guaranteed I/O
success. Read-only writes fail even for zero limits; flush is rejected before
input-only-stream stdio. Creation/truncation APIs retain their prior behavior.
This does not add general update/append modes, Unicode paths, owned strings,
thread safety, or complete language memory safety.
