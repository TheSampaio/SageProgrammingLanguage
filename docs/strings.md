# Strings (v0.39.1)

Sage has two string literal forms and nothing else:

```sage
use text;

func main(): none
{
    var world: str = "World";                    // plain literal: always text
    var message: Text = $"Hello {world}";        // interpolation: builds an owned Text
    message.print_line();                        // Hello World
}
```

| Form | Type | Meaning |
| --- | --- | --- |
| `"Hello {x}"` | `str` | A constant. Braces are ordinary characters; nothing is interpolated. |
| `$"Hello {x}"` | `Text` | A new owner, sized exactly to the result. Needs `use text;`. |

`str` stays the legacy C string (`char*`) for literals and native boundaries.
`Text` is the owned, checked string described in [text](text.md).

## How interpolation behaves

* Each `{expression}` is evaluated **exactly once**, left to right (ordered temporaries),
  and the values are passed to one `sage_text_format` call. The C result is measured first and then written into a
  fresh allocation, so there is no fixed buffer, no truncation and no overflow.
* There is no shared or static state. Nested calls, loops, recursion and several
  interpolations in one function each get an independent owner. (Before v0.39.0 every
  site used `static char buf[512]`, so two results from one site overwrote each other.)
* The result is an **owning** value, so it follows the existing owner rules: it must
  directly initialize a local `Text` (S214), is freed once at scope exit, moves with
  `var b: Text = a;`, and can be returned with `return made;`.
* Allocation failure (or a result larger than 4 GiB) yields an empty owner, like
  `text::from`; check `owner != null` when that matters.
* An expression must be a number, `b8` or `str`; anything else is S216. A `NULL` `str`
  prints as an empty string. Resources, borrows and structs keep their usual S191/S173/S196
  rejections.
* Escapes follow C because literal text reaches the C format string: `\n` and `\"` work, but
  `\0` ends the result early, C trigraphs such as `??/` are rewritten by the C compiler, and a
  backslash directly before `{` is not a brace escape. Plain `"..."` literals share these
  C escape rules. `{` always opens an expression inside `$"..."`, there is no brace escape
  yet, and an expression cannot contain `"` (a nested string literal ends the literal, S010).

Passing a result straight to a call (`show($"x{n}")`) is rejected by the existing
"bind producer results before the call" rule (S194). Bind it, then pass `&text`.

## Format specifiers

`{value:spec}` where `spec` is `[flags][width][.precision][conversion]`; width and
precision take at most four digits. The conversion must fit the type (S217):

| Type | Conversions | Flags | Default |
| --- | --- | --- | --- |
| signed integers, `b8` | `d` | `-` `+` space `0` | `d` |
| unsigned integers | `u` `x` `X` `o` | `-` `+` space `0` `#` | `u` |
| `f32`, `f64` | `f` `e` `E` `g` `G` | all | `f` |
| `str` | `s` | `-` only | `s` |

`{pi:2f}` is shorthand for two decimals (`{pi:.2f}`). Arguments are cast to the width
their conversion expects, so `%n`, `%s` on a number or a mismatched width cannot be
produced.

## Native string contracts

* **Sage to C:** a `str` is passed as `char*` and must be a valid NUL-terminated string
  for the duration of the call. The compiler never infers ownership of an arbitrary
  `char*`.
* **`Text` to C:** `Text` is length-prefixed and every byte is data, but each owner also
  keeps one terminator byte past its capacity (rewritten after every mutation). The unsafe
  `text::as_str(&value)` returns that storage as a `str` for legacy APIs. It is valid only
  while the owner is alive and unmodified, ends early at an embedded zero byte, and must
  not be stored. Encoding is not validated; bytes are treated as UTF-8-agnostic data.
* **C to Sage:** copy with `text::from(str)` (measured with a `u32` bound); the copy is
  owned and freed by Sage. Native code that returns an owned buffer needs an explicit
  `resource` declaration instead.

## Migration from v0.38.x

Plain `"... {x} ..."` strings used to interpolate. They now compile unchanged but print
the braces literally, so search for `"` strings containing `{` and:

1. add `use text;`,
2. write `var line: Text = $"... {x} ...";`,
3. use `line.print_line()` (or `append`, `equals_str`, ...) instead of passing it as `str`.

Legacy shell helpers that need a `str` use `unsafe text::as_str(&command)`; see the
rewritten `http_client` for an example.

## Validation and limits

`tests/interpolation.ps1` runs a program through an allocation tracker (105 owners,
each released once, no static buffer or `snprintf` in the generated C), a 604-byte result,
and 14 rejected programs; `Sandbox/tests/interpolation.test.sg` covers values and
format specifiers; `tests/fixtures/text-native.c` covers `sage_text_format`, the
terminator and allocation failure. AddressSanitizer and UndefinedBehaviorSanitizer cannot
run on this Windows GCC (no `libasan`/`libubsan`), so memory behavior is validated by the
allocation tracker only. `Text` itself makes no cross-thread guarantees; interpolation
shares no state, so independent threads may each use their own owners.
