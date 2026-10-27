# Sage 0.19.0 standard-library layout validation

Validated on Windows, 2026-10-25:

- Debug and Release builds: zero warnings/errors; all 20 existing PowerShell
  regression scripts passed in each configuration.
- Sandbox: 23 test files, 154 passing assertions in each configuration.
- New runtime-layout checks: only `.sg` files recursively in std; renamed HTTP
  modules import successfully; old imports fail with S007; relocated compiler and
  project paths with spaces work without native headers inside std.
- `dotnet publish -c Release` contains `Runtime/include/sage_byte_file.h` and
  passes the same relocation checks.
- Native byte-file checks retain 45 assertions under strict C11 warnings, 18
  Sage integration assertions, persisted content, exclusive reopen after close,
  and ten rejected invalid programs.

Neither HTTP client requests nor the HTTP server were started. This change
renames the existing unsafe modules and separates native implementation files;
it does not harden networking or establish complete memory safety.

A subsequent build-only investigation reproduced acceptance of a scalar cast
as a fixed-array argument. The new array-cast regression was intentionally red
on 0.19.0 and is work for the next increment, outside the 20-script baseline above.
