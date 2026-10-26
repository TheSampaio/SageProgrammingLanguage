# Owned byte files (v0.23.0)

`use byte_file;` imports a bounded file API. `ByteFile` owns an opaque native state
and follows normal move, borrow, return, and cleanup contracts. Existing unsafe
`file` wrappers are unchanged.

| Operation | Contract | Outcome |
|---|---|---|
| `empty()` | Returns a local owner | Empty handle; no file is opened |
| `create(path: str)` | **unsafe**, returns an owner | Creates/truncates a binary `w+b` file; empty on failure |
| `create_bytes(path: u8[260], length: u16)` | Safe fixed-array value parameter, returns an owner | Creates/truncates using 1..259 printable ASCII bytes; empty on failure |
| `open_read_bytes(path: u8[260], length: u16)` | Safe fixed-array value parameter, returns an owner | Opens an existing binary file for reading (`rb`), without creating/truncating it |
| `is_open(value: &ByteFile)` | Shared borrow | Whether its native stream is open |
| `can_write(value: &ByteFile)` | Shared borrow | Whether the open handle's mode permits output |
| `read_byte(value: &mut ByteFile)` | Mutable borrow | A `ByteRead` value |
| `write_byte(value: &mut ByteFile, byte: u8)` | Mutable borrow | `true` on successful byte output |
| `read_buffer(value: &mut ByteFile, buffer: &mut ByteBuffer, limit: u32)` | Exclusive file and target borrows | `ByteTransfer`; replaces the target with received bytes |
| `write_buffer(value: &mut ByteFile, buffer: &ByteBuffer, limit: u32)` | Exclusive file and shared source borrows | `ByteTransfer`; writes an initialized source prefix |
| `seek_start(value: &mut ByteFile)` | Mutable borrow | `true` on successful positioning |
| `flush(value: &mut ByteFile)` | Mutable borrow; output-capable mode | `true` on successful buffer flushing; false in read-only mode |
| `close(value: ByteFile)` | **Consumes** the owner | Reports native close success; consumes even on failure |

`ByteRead` contains `value: u8`, `has_value: b8`, and `failed: b8`:

- Data: `has_value=true`, `failed=false`; all bytes, including 0 and 255, are valid.
- Clean end of file: both flags are false.
- Empty handle or I/O/positioning failure: `has_value=false`, `failed=true`.

`value` is meaningful only when `has_value` is true. Empty owners fail write,
seek, flush, and close without invoking native I/O. False write/flush status does
not promise that no earlier buffered data reached the file. Errors are per
operation; the adapter clears native stream indicators before positioning.

## Existing files in read-only mode

Since v0.23.0, `open_read_bytes` accepts the same 1..259 printable ASCII prefix of
a `u8[260]` value parameter as `create_bytes`. It uses binary read mode `rb`.
Missing/unreadable files, invalid prefixes and allocation failure return an empty
owner. Existing contents are not truncated and missing files are not created.
The read mode also supports sources marked read-only by the current Windows
filesystem; native open can still fail for ordinary platform reasons.

These constructors produce the same non-Copy `ByteFile` owner. Read-only mode
permits byte/block reads, positioning and consuming close. `write_byte` and
`flush` return false; `write_buffer` returns count=0, eof=false, failed=true,
including for limit zero. The runtime rejects these operations before native
output or positioning, preserving the input position and source buffer.
Restrictions are runtime mode checks, not distinct Sage types.

`can_write(&file)` reports a capability fixed by acquisition and preserved through
moves/returns: true for open `create`/`create_bytes` handles, false for read-only
and empty owners. It does not test OS permissions or guarantee future output
success. Borrow, move, and close rules remain unchanged; a consumed owner cannot
be queried again (S140).

```sage
func open_existing(): ByteFile
{
    var path: u8[260] = { 100, 97, 116, 97, 46, 98, 105, 110 }; // data.bin
    var length: u16 = 8;
    var owner: ByteFile = byte_file::open_read_bytes(path, length);
    return owner;
}
```

This separation avoids incompatible `fflush` calls on input-only streams. Modes
follow C11 draft N1570 [§§7.21.5.2–3](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf).
`tests/byte-file-read-only.ps1` verifies actual read-only source preservation,
native output-call counters, allocation/close failures and exact cleanup across
generated translation units.

## Block transfers

Since v0.22.0, `ByteTransfer { count: u32; eof: b8; failed: b8; }` describes a
single bounded native block operation. `byte_file` imports `byte_buffer` as a
dependency. The public surface exposes no data pointers.

| Situation | Count and flags | Target buffer after read |
| --- | --- | --- |
| Requested bytes transferred | `count=limit`, normally both flags false | Exactly `count` initialized bytes |
| Clean EOF during read | `count <= limit`, `eof=true`, `failed=false` | Exactly `count` bytes, including zero |
| Read error after partial progress | Count remains meaningful, `failed=true`; EOF flag remains independent | Only the received prefix is accessible |
| Invalid/empty owner or excessive limit | Zero, `eof=false`, `failed=true` | Previous contents/length preserved |
| Positioning failure before native transfer | Zero, `eof=false`, `failed=true` | Previous contents/length preserved |
| Zero limit with valid owners and a permitted operation | Zero, both flags false; no native I/O or EOF probe | Previous contents/length preserved |

`read_buffer` requires `limit <= capacity`. After native input starts it replaces
length with the actual byte count even on EOF/error; it never appends or exposes
old suffix bytes. A clean short read can therefore contain useful data. An exact
read does not predict EOF: another nonzero read may be required to observe it.
Unexpected short input with neither native EOF nor error is treated as failure.

`write_buffer` requires `limit <= length`, so spare capacity is never read. It
does not mutate the source. `eof` is always false; a short write is a failure,
including when the native error indicator is absent. A nonzero count can accompany
failure. Count reports bytes accepted by the C stream, which may still be buffered;
use flush and consuming close to observe their separate results. Error paths do
not roll back transferred bytes. File position after a native error is not
guaranteed; this API performs no automatic retry or recovery.

```sage
func receive(file: &mut ByteFile, buffer: &mut ByteBuffer): ByteTransfer
{
    var limit: u32 = byte_buffer::capacity(&buffer);
    return byte_file::read_buffer(&mut file, &mut buffer, limit);
}
```

The implementation calls `fread`/`fwrite` with one-byte elements, retaining exact
byte counts. Transfer status is private per-file state and the wrapper captures
it immediately while the exclusive file borrow is held; no shared global status
or allocated result handle is used. Semantics follow C11 draft N1570
[§§7.21.8.1–2](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf).

`tests/byte-file-buffer.ps1` covers deterministic partial reads/writes and errors,
preserved buffers before I/O, initialized prefixes after I/O, all-byte persisted
roundtrip, and static ownership/borrow rejection.

```sage
use byte_file;

func write_one(value: &mut ByteFile, byte: u8): b8
{
    return byte_file::write_byte(&mut value, byte);
}

unsafe func example(): b8
{
    var owner: ByteFile = byte_file::create("example.bin");
    if (owner == null) { return false; }
    var byte: u8 = 255;
    var written: b8 = write_one(&mut owner, byte);
    var closed: b8 = byte_file::close(owner);
    return written && closed;
}
```

Acquisition using `create(str)` stays unsafe because legacy `str` has an unchecked `char*` lifetime.
The caller must supply a valid, NUL-terminated path for the duration of the call,
with the platform C runtime's path encoding. Creation truncates existing content.
There is no safe general path/string API yet. Once valid ownership is established,
the byte operations above require neither legacy strings nor raw pointer syntax.
Unsafe direct native calls or fabricated resource handles can violate this contract.

Since v0.20.0, `create_bytes` provides a safe bounded alternative. Its fixed array
is copied on Sage function entry. `length` selects 1..259 bytes; every selected
byte must be printable ASCII (32..126). Zero, controls, non-ASCII bytes, empty
prefixes, and lengths at or beyond 260 are rejected before any file is opened.
The remaining array bytes are ignored. No input NUL terminator is needed: the
native adapter adds one inside its own 260-byte stack buffer.

```sage
func example_bytes(): b8
{
    // ASCII for example.bin; the unused array entries are zero-filled.
    var path: u8[260] = { 101, 120, 97, 109, 112, 108, 101, 46, 98, 105, 110 };
    var length: u16 = 11;
    var owner: ByteFile = byte_file::create_bytes(path, length);
    if (owner == null) { return false; }
    return byte_file::close(owner);
}
```

This is a bounded ASCII byte-path interface, not Unicode/text ownership or a
portable OS path model. Printable bytes can still form invalid platform paths;
native open failure returns an empty owner. `create`/`create_bytes` create or
truncate; allocation failure after a successful create can leave a truncated
file. `open_read_bytes` preserves contents even on state-allocation failure and
closes its already-opened input stream. Prefix validation failure guarantees no
native open. Scalar/pointer-to-array
casts cannot fabricate the fixed array: unsupported array casts report S188.

The adapter uses a positioning barrier before read, write, and flush, preventing
invalid input/output transitions on C update streams. Non-seekable streams fail
the operation. Successful flush concerns C buffering, not durable storage against
power failure. The behavior follows C11 draft N1570 §§7.21.5.2–3 and 7.21.9.2;
see [the C draft](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf).

Explicit close clears the stored stream before `fclose`. Whether close succeeds
or fails, it is not retried; normal resource cleanup then frees the native state.
This follows `fclose` disassociation in N1570 §7.21.5.1. Implicit scope/early-return
cleanup closes and frees best-effort, with no recoverable close result; use
explicit `close` when its result matters. Ownership is unavailable afterward,
including when close reports failure. Process termination still does not unwind.

The C helper `Runtime/include/sage_byte_file.h` is part of the compiler's trusted
implementation and is copied beside compiler build/publish outputs. GCC resolves
it relative to the compiler executable, including after relocation. The `std`
directory contains only `.sg` sources; a local std override needs `byte_file.sg`
and its `byte_buffer.sg` dependency, without copying native headers into it.
Compiler distributions must retain the
separate `Runtime/include` directory.
The adapter requires 8-bit C bytes and has been validated with the current Windows
GCC toolchain; broader platform behavior has not been operationally demonstrated.

The interface intentionally omits text encoding, append/general update modes, arbitrary
seeks, buffer slices, detailed OS error codes, and safe general path acquisition.
Tests are `Sandbox/tests/byte_file.test.sg` (safe empty-owner and rejected paths) and
`tests/byte-file.ps1` (native failure injection, all-byte roundtrip, persisted
content, exclusive reopen after explicit/early-return close, paths with spaces,
safe ASCII path creation, prefix validation before I/O, and invalid
ownership/borrow/native calls). `tests/array-casts.ps1` verifies unsupported casts
are rejected before C generation without executing invalid programs.
