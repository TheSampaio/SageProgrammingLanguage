# Sage 0.33.3 Sandbox application validation

Validated on Windows, 2026-10-26:

- Debug and Release builds: 43 PowerShell scripts passed. Sandbox passed
  30 files with 291 assertions in each configuration.
- `tests/sandbox-apps.ps1` builds the following against a copy of the shipped
  std and runs them where noted:
  - `Sandbox/src/main.sg`, the savings calculator. It uses `Text`, `IntVector`,
    and `byte_file` Text I/O. The test checks its output and that the CSV
    report it writes has 11 lines.
  - `examples/greeting.sg`, which reads bounded input from a piped stdin with
    CRLF line endings and prints `Hello, Ada!`.
  - `examples/http_echo.sg`, which is built only because it does not exit.
  - `examples/owned_byte_buffer.sg`, which prints `Buffer value: 42`.
- The test fails if `unsafe {` or `unsafe func` appears in any of these sources.
  `examples/native_resource_contract.sg` is excluded because it demonstrates
  native adoption on purpose.

No compiler or std change; the version stays 0.33.3.
