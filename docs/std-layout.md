# Sage-only standard library

Since v0.19.0, every file under `Sandbox/std` is a Sage source (`.sg`). Native
runtime headers belong to `Runtime/include` beside the compiler sources. Build
and publish copy that directory beside `Sage.dll`; the native compiler resolves
it relative to the running compiler, independently of the project's location.
Since v0.38.1 build and publish also copy the `.sg` sources of `Sandbox/std`
into `std/` beside `Sage.dll`. A project without a local `std` folder (for
example one created by `sage new`) imports from there; a project's own `std`
folder still takes precedence. `tests/cli-new-project.ps1` validates this. Generated C and binaries remain build outputs.

The HTTP files and their module names have changed:

| Previous | Current |
| --- | --- |
| `http.sg`, `use http;`, `http::fetch(...)` | `http_client.sg`, `use http_client;`, `http_client::fetch(...)` |
| `server.sg`, `use server;`, `server::serve(...)` | `http_server.sg`, `use http_server;`, `http_server::serve(...)` |

The old imports report `S007`; no compatibility files are retained in `std`.
This rename preserves the existing unsafe HTTP contracts and does not change
transport or shell escaping. The runtime layout test imports both modules
without invoking their network operations.

`tests/runtime-layout.ps1` checks the Sage-only directory recursively, renamed
modules, rejection of old imports, and execution using a relocated compiler and
project whose paths contain spaces. Byte-file tests also use only `.sg` files in
their copied standard library.
