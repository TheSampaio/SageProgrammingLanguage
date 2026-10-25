# Owned byte buffers (v0.21.0)

`use byte_buffer;` imports `ByteBuffer`, an exclusive owner of a fixed-capacity
native allocation. The public interface uses no raw pointers or legacy strings.
Normal moves, borrows, owning returns, consuming parameters, and automatic
scope/early-return cleanup apply.

| Operation | Contract | Result |
| --- | --- | --- |
| `create(capacity: u32)` | Returns a local owner | Allocated buffer with length zero, or empty owner on failure |
| `empty()` | Returns a local owner | No allocation |
| `is_allocated(value: &ByteBuffer)` | Shared borrow | Whether the owner holds storage |
| `length(value: &ByteBuffer)` | Shared borrow | Number of initialized bytes, `u32` |
| `capacity(value: &ByteBuffer)` | Shared borrow | Fixed maximum length, `u32` |
| `read(value: &ByteBuffer, index: u32)` | Shared borrow | `BufferRead { value: u8; has_value: b8; }` |
| `write(value: &mut ByteBuffer, index: u32, byte: u8)` | Mutable borrow | Replaces a byte only when `index < length`; returns success |
| `push(value: &mut ByteBuffer, byte: u8)` | Mutable borrow | Appends only when `length < capacity`; returns success |
| `clear(value: &mut ByteBuffer)` | Mutable borrow | Sets length to zero, retaining capacity and allocation |
| `release(value: ByteBuffer)` | Consumes a named owner | Parameter cleanup frees the allocation |

Zero capacity is a valid allocated buffer; use `is_allocated` or `owner != null`
to distinguish it from failed acquisition. Empty owners report zero length and
capacity, reads have `has_value=false`, writes/pushes fail, and clear/release are
harmless. Every byte value, including zero and 255, is data.

A read outside the initialized prefix returns `has_value=false` and `value=0`.
Inspect the flag before using the byte. Spare capacity is inaccessible, including
after clear. Write cannot initialize a new position; push writes the byte before
increasing length. Failed writes/pushes preserve length and contents. Clear does
not securely erase memory; it hides prior contents from the safe API.

```sage
use byte_buffer;

func example(): b8
{
    var capacity: u32 = 4;
    var owner: ByteBuffer = byte_buffer::create(capacity);
    var byte: u8 = 42;
    var pushed: b8 = byte_buffer::push(&mut owner, byte);
    var index: u32 = 0;
    var read: BufferRead = byte_buffer::read(&owner, index);
    byte_buffer::release(owner);
    return pushed && read.has_value && read.value == 42;
}
```

Use after move/release is S140. Shared-borrow mutation is S171; consumption while
a loan is live is S178. Bind producer results before passing owners to release
(S194). Explicit release is optional when ordinary scope cleanup is sufficient;
both paths use the same resource destructor. Borrowed values never own storage.

The trusted implementation is `Runtime/include/sage_byte_buffer.h`, distributed
with the compiler outside the Sage-only std. It uses one allocation for metadata
and byte storage, checks allocation-size addition before allocating, and never
exposes a pointer through its safe API. Capacity is limited to `u32`; native
adapters require 8-bit bytes and at least 32-bit `size_t`. Allocation can fail at
any requested capacity. No resize, slices, clone, encoding, text termination,
thread safety, or public native-buffer conversion is provided yet. Unsafe native
calls/fabricated handles retain their own obligations. This is a buffer primitive,
not a claim of complete language memory safety.

`Sandbox/examples/owned_byte_buffer.sg` is runnable without file side effects.
Tests cover bounds, initialization, all 256 bytes, allocation failure/overflow,
exact cleanup across generated translation units, rejected ownership/borrow
operations, and a safe buffer-to-byte-file roundtrip with independently verified
persisted content. See `tests/byte-buffer.ps1` and
`Sandbox/tests/byte_buffer.test.sg`. File operations are currently per-byte; bulk
buffer I/O and general owned strings remain later work.
