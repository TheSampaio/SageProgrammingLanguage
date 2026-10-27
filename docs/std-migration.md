# Standard library migration audit (2026-10-26)

Phase 4 migrates `Sandbox/std` toward ownership-aware APIs. This audit lists
every public std function that exposes raw pointers, unbounded buffers, shell
interpolation, or unclear ownership, and how each is handled.

Status values:

- **enforced**: a safe signature whose contract the wrapper or the compiler checks.
- **unsafe-by-design**: kept as `unsafe func`; the caller upholds the documented
  precondition. A safe alternative is named when one exists.
- **migrated**: a safe replacement exists and the legacy API is deprecated in docs.
- **open**: a known gap without a replacement yet.

Modules not listed (`byte_buffer`, the safe parts of `byte_file`, `test`) expose
no raw pointers and use owned resources or plain values.

## Audit at the start of Phase 4

| Module | API | Risk | Status |
| --- | --- | --- | --- |
| `memory` | `alloc(size): none*`, `release(ptr)` | raw allocation, manual pairing | unsafe-by-design |
| `file` | `open(path, mode): FILE*`, `write_text(FILE*, str)`, `close(FILE*)` | raw handle, no ownership, manual close | unsafe-by-design (legacy) |
| `byte_file` | `create(path: str)` | trusts a terminated `str` path | unsafe-by-design; `create_bytes` is enforced |
| `console` | `read_line(buffer: str)` | unbounded `scanf("%s")` into caller storage | unsafe-by-design, no replacement |
| `console` | `run(command: str)` | executes a shell command line | safe signature; shell authority by design |
| `console` | `print`, `print_line`, `clear`, `read` | `str` trusted as terminated | enforced (fixed format strings) |
| `http_client` | `fetch`, `post`, `download` | shell interpolation without escaping | unsafe-by-design, no replacement |
| `http_client` | `get_into(url, buffer: str, max)` | shell interpolation, caller-sized raw buffer | unsafe-by-design, no replacement |
| `http_server` | `serve(port, body)` | raw sockets, unchecked errors, never returns | unsafe-by-design, no replacement |
| (none) | owned strings | no safe text type | open |
| (none) | dynamic collections | no growable owned container | open |

The rows below record each migration increment as it lands.

## Migration log

| Version | Change | Resulting status |
| --- | --- | --- |
| 0.33.0 | New `text` module: owned `Text` with fixed capacity, append/compare/print and bounded `read_line` ([text](text.md)) | owned strings: enforced; `console.read_line`: migrated to `Text.read_line` |
| 0.33.1 | New `int_vector` module: owned growable `IntVector` with a fixed limit, checked `get`/`set`/`pop` and `VectorGrow` outcomes ([int vector](int-vector.md)) | dynamic collections: enforced (`i32` only) |
| 0.33.2 | `byte_file` gains `create_text`/`open_read_text` paths and `write_text`/`read_text` contents ([byte files](byte-files.md)); `file` and `memory` doc comments point to the owned alternatives | `file`: migrated (deprecated, kept unsafe for FFI demos); `memory`: unsafe-by-design with safe alternatives (`text`, `int_vector`, `byte_buffer`, plain values) |
| 0.33.3 | Safe `http_client.get/post/download` over `Text` without a shell; owned `http_server.HttpListener` with bounded `accept` and `respond` ([HTTP](http.md)); legacy `post`/`download` renamed `post_shell`/`download_shell` | `http_client` shell helpers: migrated (kept unsafe); `http_server.serve`: migrated to `HttpListener` (kept unsafe) |
| (apps) | `Sandbox/src/main.sg` uses `text`, `int_vector` and `byte_file` Text I/O; new `examples/greeting.sg` (bounded input) and `examples/http_echo.sg` (owned listener); `tests/sandbox-apps.ps1` builds/runs them and rejects `unsafe` in app code | Sandbox apps: safe |

## Status after Phase 4

| API | Status |
| --- | --- |
| `text`, `int_vector`, `byte_buffer`, `byte_file` (Text/byte paths), `http_client.get/post/download`, `http_server.HttpListener`, `console.print*`/`read`, `test` | enforced (safe) |
| `memory.alloc/release` | unsafe-by-design (FFI); safe alternatives listed above |
| `file.*`, `byte_file.create(str)` | unsafe-by-design, deprecated for apps (use `byte_file` Text paths) |
| `console.read_line(str)` | unsafe legacy; migrated to `Text.read_line` |
| `http_client.fetch/post_shell/download_shell/get_into`, `http_server.serve` | unsafe legacy; migrated to the Text/listener APIs |
| `console.run(str)` | safe signature, executes a shell command by design; never pass untrusted text |

Open: collections for other element types (no generics), Text slicing/formatting
and UTF-8 validation, non-Windows HTTP backends, HTTP status reporting.
