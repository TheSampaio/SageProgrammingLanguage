# Safe HTTP client and server (v0.33.3)

`http_client` and `http_server` now have safe APIs built on [`Text`](text.md).
Application code needs no `unsafe`. The old `str` helpers remain as
`unsafe func` for compatibility. Both backends are Windows only; this matches
the existing Winsock server.

## Client: curl without a shell

```sage
use http_client;
use text;

func main(): none
{
    var url: Text = text::from("https://example.com/api?q=sage");
    var body: Text = text::create(4096);              // the response limit
    var result: HttpResult = http_client::get(&url, &mut body);
    if (result.ok) { body.print_line(); }
    else if (result.truncated) { /* body holds the first 4096 bytes */ }
}
```

| Function | Contract |
| --- | --- |
| `get(url: &Text, response: &mut Text): HttpResult` | GET. Replaces `response` with up to its capacity of body bytes |
| `post(url: &Text, data: &Text, response: &mut Text): HttpResult` | POST. Sends `data` verbatim (any bytes) over curl's stdin with `--data-binary @-` |
| `download(url: &Text, path: &Text): HttpResult` | Saves the body to `path` (`--output`) |

`HttpResult { ok, exit_code, truncated, rejected, unavailable }`:

- `ok`: curl ran and exited with 0. HTTP error statuses such as 404 are not
  curl failures, so check the body.
- `exit_code`: curl's exit status, or -1 if curl did not run.
- `truncated`: the response did not fit in the target. The rest was read and
  discarded, so curl never blocks.
- `rejected`: invalid input; nothing was started. This covers a URL that is not
  `http://`/`https://`, a URL that contains spaces, control bytes or non-ASCII,
  a URL longer than 8192 bytes, a path that is empty, longer than 259 bytes, not
  printable ASCII, or starts with `-`, an empty owner, and a command line longer
  than 32767 characters.
- `unavailable`: curl could not be started, or this platform has no backend.

How it is hardened (`Runtime/include/sage_http_client.h`):

- No shell. `CreateProcessA` starts `curl` directly. Every argument is quoted
  with the Windows argv rules: backslashes are doubled only before a quote, and
  quotes are escaped. `"`, `&`, `|`, `^`, `%VAR%` and backslashes reach curl as
  data. The test confirms this with a curl test double that echoes its argv.
- Fixed leading options: `-q` (ignore `.curlrc`), `--silent`,
  `--proto =http,https`, and `--proto-redir =http,https`.
- POST data never appears on the command line. It goes through a pipe, so its
  size is not limited by the command line and `@file` is never interpreted.
- Output is bounded by the caller's `Text` capacity. stderr is discarded.
- Windows looks for `curl.exe` in this order: the application directory, the
  current directory, the system directories, then `PATH`. A `curl.exe` placed
  beside the program or in its working directory therefore takes precedence;
  deployments should control those directories.

Migration from the legacy helpers:

| Legacy (`unsafe`, shell-interpolated) | Safe replacement |
| --- | --- |
| `fetch(url: str)` (prints the body) | `get(&url, &mut body)` then `body.print()` |
| `post(url: str, data: str)`, renamed `post_shell` | `post(&url, &data, &mut body)` |
| `download(url: str, path: str)`, renamed `download_shell` | `download(&url, &path)` |
| `get_into(url: str, buffer: str, max: u64)` | `get(&url, &mut body)` |

Existing unsafe calls to `http_client::post(str, str)` or
`download(str, str)` now report argument errors. Rename them to the `_shell`
forms or migrate to `Text`.

## Server: owned listener

```sage
use http_server;
use text;

func main(): none
{
    var server: HttpListener = http_server::listen_local(8080); // 127.0.0.1 only
    var request: Text = text::create(1024);
    var body: Text = text::from("Hello from Sage");
    var running: b8 = server.is_listening();      // empty owner on failure
    while (running)
    {
        if (server.accept(&mut request)) { server.respond(200, &body); }
    }
} // drop closes the sockets and calls WSACleanup once
```

| Operation | Contract |
| --- | --- |
| `listen_local(port: u16)` / `listen(port: u16)` | Binds 127.0.0.1 or all IPv4 interfaces with `SO_EXCLUSIVEADDRUSE`. Port 0 picks a free port. Failure returns an empty owner, with the socket closed and Winsock released |
| `is_listening()`, `port()` | Shared borrow. `port()` reports the actually bound port |
| `accept(request: &mut Text): b8` | Blocks for one client. Replaces `request` with the request head (request line and headers), bounded by capacity and 8 KiB, with a 5 s receive timeout. An unanswered previous client is closed first |
| `respond(status: u16, body: &Text): b8` | Sends `HTTP/1.1 <status>` with `text/plain; charset=utf-8`, `Content-Length`, and `Connection: close`, then closes the client. Returns true only if every byte was sent. The status must be in 100..599 |
| `http_server::close(value: HttpListener)` | Consumes the listener. The same cleanup runs at scope exit |

`HttpListener` is a declared native resource
(`drop sage_http_listener_drop`). Moves, borrows, use-after-close (S140),
`close` as a method (S159), and shared-borrow `accept`/`respond` (S171) follow
the ordinary resource rules. Every error path inside the native implementation
closes what it opened.

The legacy `serve(port: u16, body: str)` is still an `unsafe func`. It never
returns and does not check socket errors. Request bodies are not read, and
there is no routing, keep-alive, TLS, IPv6, or concurrency.

## Validation

`tests/http.ps1` covers:

- exact argv and stdin transfer through a curl test double, including injection
  characters, a path with spaces and backslashes, truncation, exit-code
  propagation, and four rejected inputs;
- a live loopback round trip, in which a Sage server answers a .NET client and a
  safe Sage client that uses the real curl;
- `clash=0` when a second listener binds the same port, an ephemeral port,
  `respond` without a client, and a rebind of the port after the listener is
  dropped;
- 10 rejected programs.
