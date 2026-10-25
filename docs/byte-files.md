# Owned byte files (v0.20.0)

`use byte_file;` imports a bounded file API. `ByteFile` owns an opaque native state
and follows normal move, borrow, return, and cleanup contracts. Existing unsafe
`file` wrappers are unchanged.

| Operation | Contract | Outcome |
|---|---|---|
| `empty()` | Returns a local owner | Empty handle; no file is opened |
| `create(path: str)` | **unsafe**, returns an owner | Creates/truncates a binary `w+b` file; empty on failure |
| `create_bytes(path: u8[260], length: u16)` | Safe fixed-array value parameter, returns an owner | Creates/truncates using 1..259 printable ASCII bytes; empty on failure |
| `is_open(value: &ByteFile)` | Shared borrow | Whether its native stream is open |
| `read_byte(value: &mut ByteFile)` | Mutable borrow | A `ByteRead` value |
| `write_byte(value: &mut ByteFile, byte: u8)` | Mutable borrow | `true` on successful byte output |
| `seek_start(value: &mut ByteFile)` | Mutable borrow | `true` on successful positioning |
| `flush(value: &mut ByteFile)` | Mutable borrow | `true` on successful buffer flushing |
| `close(value: ByteFile)` | **Consumes** the owner | Reports native close success; consumes even on failure |

`ByteRead` contains `value: u8`, `has_value: b8`, and `failed: b8`:

- Data: `has_value=true`, `failed=false`; all bytes, including 0 and 255, are valid.
- Clean end of file: both flags are false.
- Empty handle or I/O/positioning failure: `has_value=false`, `failed=true`.

`value` is meaningful only when `has_value` is true. Empty owners fail write,
seek, flush, and close without invoking native I/O. False write/flush status does
not promise that no earlier buffered data reached the file. Errors are per
operation; the adapter clears native stream indicators before positioning.

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
native open failure returns an empty owner. Both constructors create/truncate;
allocation failure after a successful open can leave a truncated file. Only
prefix validation failure guarantees no native open. Scalar/pointer-to-array
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
without copying native headers into it. Compiler distributions must retain the
separate `Runtime/include` directory.
The adapter requires 8-bit C bytes and has been validated with the current Windows
GCC toolchain; broader platform behavior has not been operationally demonstrated.

The interface intentionally omits text encoding, append/open modes, arbitrary
seeks, byte buffers, detailed OS error codes, and safe general path acquisition.
Tests are `Sandbox/tests/byte_file.test.sg` (safe empty-owner and rejected paths) and
`tests/byte-file.ps1` (native failure injection, all-byte roundtrip, persisted
content, exclusive reopen after explicit/early-return close, paths with spaces,
safe ASCII path creation, prefix validation before I/O, and invalid
ownership/borrow/native calls). `tests/array-casts.ps1` verifies unsupported casts
are rejected before C generation without executing invalid programs.
