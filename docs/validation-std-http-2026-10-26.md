# Sage 0.33.3 safe HTTP validation

Validated on Windows, 2026-10-26:

- Debug and Release builds: 42 PowerShell scripts passed; Sandbox passed
  30 files / 291 assertions in each configuration.
- `tests/http.ps1`:
  - argv transfer without a shell, checked by a curl test double placed in the
    application directory. A URL with `"`, `&`, `|`, `^`, `%PATH%` and a
    trailing backslash arrives verbatim. POST data arrives verbatim on stdin.
    A download path with spaces and a backslash reaches `--output`.
    Truncation, curl exit status 7, and rejection of a non-HTTP scheme, a
    URL containing a space, an option-like path and an empty response owner
    all report the expected `HttpResult`.
  - live loopback round trip: a safe Sage `HttpListener` serves a .NET
    `WebClient` and a safe Sage client that runs the real system curl. Both
    receive the echoed request line.
  - listener resource behavior: a second bind of the same port fails (empty
    owner), an ephemeral port is reported, `respond` without a client fails,
    the server exits 0 after two requests, and the port can be rebound
    immediately after the listener is dropped.
  - 10 rejected programs: S170, S172, S164 (shell helpers, legacy `serve`,
    native calls), S140, S159, and S171.
- `tests/runtime-layout.ps1` still compiles a program that imports
  `http_client`, `http_server` and `byte_file` together from a relocated
  compiler. The client's `windows.h` and the server's `winsock2.h` coexist.
- `tests/native-boundaries.ps1` uses the renamed `post_shell` and
  `download_shell`. Both still require unsafe.

No compiler code changed. Backends exist for Windows only. On other platforms
the client reports `unavailable`, and listeners are empty owners.
