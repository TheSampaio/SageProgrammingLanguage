# Owned text (v0.33.0)

`use text;` imports `Text`, an exclusive owner of fixed-capacity byte text, and
the `TextByte` / `LineRead` result structs. Creating, appending, comparing,
printing and reading lines need no `unsafe` code. The public interface exposes
no raw pointers. Normal moves, borrows, owning returns, consuming parameters,
reassignment and automatic scope/early-return cleanup apply, exactly as for
[`ByteBuffer`](byte-buffers.md).

```sage
use text;

func main(): none
{
    var name: Text = text::from("Sage");      // capacity == length (4)
    var greeting: Text = text::create(32);    // empty, capacity 32
    greeting.append_str("Hello, ");
    greeting.append(&name);
    if (!greeting.push(33)) { /* full: contents unchanged */ }
    greeting.print_line();                    // Hello, Sage!

    var moved: Text = greeting;               // greeting is unavailable now (S140)
    var same: b8 = moved.equals_str("Hello, Sage!");
} // every owner is freed once here
```

## Operations

Methods are `method func` declarations, so `value.length()` is the same call as
`text::length(&value)`. Consuming operations (`release`) stay qualified.

| Operation | Receiver | Result |
| --- | --- | --- |
| `text::create(capacity: u32)` | - | Empty text, or empty owner on allocation failure |
| `text::from(value: str)` | - | Copy of the string's bytes; capacity = length |
| `text::empty()` | - | Empty owner, no allocation |
| `copy()` | `&Text` | Independent owner with equal contents and capacity |
| `is_allocated()` | `&Text` | Whether the owner holds storage (`owner != null` also works) |
| `length()`, `capacity()` | `&Text` | Initialized bytes / fixed maximum, `u32` |
| `byte_at(index: u32)` | `&Text` | `TextByte { value, has_value }`; `has_value=false`, `value=0` outside `[0, length)` |
| `append_str(other: str)` | `&mut Text` | All-or-nothing append within capacity |
| `append(other: &Text)` | `&mut Text` | All-or-nothing append of another owner's bytes |
| `push(byte: u8)` | `&mut Text` | Appends one byte within capacity |
| `clear()` | `&mut Text` | Length 0; capacity kept; old bytes hidden, not erased |
| `compare(other: &Text)` | `&Text` | Unsigned byte-wise order: -1, 0, 1 |
| `equals(other: &Text)`, `equals_str(other: str)` | `&Text` | Content equality |
| `print()`, `print_line()` | `&Text` | Writes the bytes to stdout; false on output error |
| `read_line()` | `&mut Text` | Replaces contents with one stdin line, see below |
| `text::release(value: Text)` | consumes | Parameter cleanup frees the storage |

Capacity is fixed at creation; there is no implicit growth or global allocator.
A failed append/push returns `false` and leaves the text unchanged. Every byte
value, including 0 and 255, is data: `Text` is not NUL-terminated, and output
uses its length. `from`/`append_str`/`equals_str` read a legacy `str` up to its
terminator; that `str` contract (a valid terminated string, e.g. a literal) is
trusted inside the wrapper. Empty owners behave as empty text for reads and
comparisons; appends and pushes to them fail, and an empty source owner makes
`append` fail.

## Bounded line input

`read_line` replaces the unsafe `console::read_line(buffer: str)`:

```sage
var line: Text = text::create(80);
var result: LineRead = line.read_line();
// LineRead { count: u32; eof: b8; truncated: b8; failed: b8; }
```

* The text is cleared, then bytes up to `\n` are read from stdin. A final `\r`
  before `\n` is removed, so `\n` and `\r\n` input behave the same.
* Bytes beyond capacity are consumed and discarded and `truncated=true`; the next
  call starts at the next line. A `\r\n` terminator does not count as overflow.
* `eof=true` when input ended before a newline. `eof && count == 0` means no
  more input; `eof` with `count > 0` is a last unterminated line.
* `failed=true` for a stream error or an empty owner.

## Ownership and borrow errors

| Mistake | Code |
| --- | --- |
| Use after move, release, or consuming call | S140 |
| Mutating through `&Text` (`append_str`, `read_line`, ...) | S171 |
| `t.append(&t)` (shared and mutable borrow of one owner) | S172 |
| Releasing while a borrow is live | S178 |
| Moving out of a borrow | S191 |
| `text::release(text::from(...))` (anonymous owner) | S194 |
| `t.release()` (consuming method) | S159 |
| Passing `Text` where `str` is expected | S161 |
| Calling `text_native::*` without unsafe | S164 |

## Native implementation and limits

`Runtime/include/sage_text.h` stores length, capacity and bytes in one
allocation, checks capacity arithmetic before allocating, measures `str`
arguments with a `u32` bound, and copies with `memmove`. Capacity is limited to
`u32`. There is no resize, slicing, formatting into `Text`, encoding validation
(bytes are not checked as UTF-8), conversion to `str`, or thread safety yet.
A `$"..."` literal now builds a `Text` (see [strings](strings.md)); print it with `print`/`print_line`. Every owner keeps one extra terminator byte past its capacity, so `unsafe text::as_str(&value)` can lend a NUL-terminated view to legacy `str` APIs.

Validated by `tests/text.ps1` (native contract incl. allocation failure and line
input edge cases, 40 Sage assertions, a real stdin pipe, exactly-once cleanup
with an allocation tracker, 13 rejected programs) and `Sandbox/tests/text.test.sg`.
