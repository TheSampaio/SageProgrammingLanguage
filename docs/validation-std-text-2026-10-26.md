# Sage 0.33.0 owned text validation

Validated on Windows, 2026-10-26:

- Debug and Release builds: 40 PowerShell scripts passed; Sandbox passed
  28 files / 254 assertions in each configuration (baseline 39 / 27 / 214).
- `tests/text.ps1`: native contract compiled with `-Wall -Wextra -Werror`
  (null owners, allocation failure, capacity overflow before allocation,
  all-or-nothing append incl. self-append, NUL/255 bytes as data, unsigned
  ordering, exact `print` bytes, and `read_line` with CRLF, truncation, an
  exact-fit `\r\n`, overflowing `\r`, empty lines and EOF).
- 40 Sage assertions from `Sandbox/tests/text.test.sg`.
- A built program reads a real stdin pipe through `Text.read_line`; output
  matches the expected count/eof/truncated flags per line.
- A test-only allocation tracker around `sage_text.h` records 10 allocations and
  10 frees across owning returns, moves, early return, consuming parameters,
  loop scopes and owner reassignment; no unknown or double free.
- 13 rejected programs fail before C generation (S140, S171, S172, S159, S178,
  S191, S194, S164, S109, S161).

No compiler code changed; the increment adds `Sandbox/std/text.sg` and
`Runtime/include/sage_text.h` on top of existing resource/borrow/method rules.
