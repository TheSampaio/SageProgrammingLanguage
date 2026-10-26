# Extension methods (v0.34.1)

Extension methods give module functions an ergonomic dot form without classes,
inheritance, `self`, or runtime dispatch:

```sage
// counter.sg
struct Counter { count: i32; }

module counter
{
    method func read(value: &Counter): i32 { return value.count; }
    method func bump(value: &mut Counter, amount: i32): none { value.count = value.count + amount; }
    method func doubled(value: i32): i32 { return value * 2; }
    func reset(value: &mut Counter): none { value.count = 0; } // not a method
}
```

```sage
// main.sg
use counter;

func main(): none
{
    var c: Counter = { count = 1 };
    c.bump(4);                      // counter::bump(&mut c, 4)
    var total: i32 = c.read();      // counter::read(&c)
    var twice: i32 = total.doubled(); // counter::doubled(total)
    counter::reset(&mut c);         // ordinary functions keep the qualified form
}
```

`value.name(args)` is resolved at compile time to exactly one module function and
then analyzed and generated as the qualified call `Module::name(receiver, args)`.
The generated C is the same direct call.

## Opt-in declaration

Only functions declared with the contextual word `method` before `func` inside a
module are eligible: `method func` or `unsafe method func`. Every other function
keeps the qualified call form only, so importing a module never turns its whole
API into methods. The marker sits on the declaration, next to the existing
`unsafe` modifier, because the receiver contract is always the first parameter
and the opt-in is a property of the function's public API rather than of each
call. `method` remains an ordinary identifier elsewhere.

A method declaration must:

* appear inside a `module` (top-level `method func` is `S157`);
* have a first parameter, the receiver (`S157` otherwise);
* use a scalar, `str`, struct, or native resource receiver, by value or through
  `&T` / `&mut T`. Raw pointer and fixed-array receivers are `S157`;
* not consume a native resource receiver. `method func close(file: File)` is
  `S157`: a consuming call must stay visible as `module::close(file)`. Other
  parameters may still consume owners; that move is visible as a named argument.

`extern` functions cannot be methods.

## Resolution

For a call `receiver.name(args)` the compiler:

1. analyzes the receiver type `R` (no ownership change happens at this step);
2. collects every `method func name` whose receiver parameter is `R`, `&R`, or
   `&mut R` (exact type, no numeric promotion) from modules declared in the
   current file or in files the current file imports directly with `use`;
3. requires exactly one candidate.

| Situation | Result |
|-----------|--------|
| One candidate | Call `Module::name(receiver, args)` |
| Candidates from two or more modules | `S158`; qualify the call as `Module::name(value, ...)` |
| Only a module not imported by this file has one | `S153`; add the `use` directive |
| A visible function `name` takes `R` but is not `method` | `S159`; mark it `method func` or call it qualified |
| `R` is a struct with a field `name` | `S156`; fields are not callable |
| Nothing matches | `S156` |

There is no precedence between modules, including a module declared in the same
file: two applicable modules always produce `S158`. Fields and methods cannot be
confused because the forms differ syntactically: `value.name` is always a field
access and `value.name(...)` is always a method call.

## Ownership and borrowing

The receiver's passing mode is the first parameter's contract:

| Receiver parameter | Lowered argument | Effect |
|--------------------|------------------|--------|
| `T` (Copy scalar or struct) | `receiver` | value copy |
| `&T` | `&receiver` | shared call-duration borrow |
| `&mut T` | `&mut receiver` | exclusive call-duration borrow |

The borrow is introduced only because the declaration says so, and the lowered
call goes through the ordinary call analysis: shared/mutable compatibility,
constant and shared-borrow mutation (`S171`), conflicting borrows in the same
call (`S172`), live loans (`S178`), unsafe callees (`S164`), argument count and
type (`S160`/`S161`), consuming arguments, and use after move (`S140`). Borrowed
receivers must be named locals or parameters, as for explicit borrows (`S175`
for fields and temporaries). A method that returns a native resource may directly
initialize a local owner, like an ordinary owning call (`S193` otherwise).

## Codes

| Code | Meaning |
|------|---------|
| S153 | Method found only in a module this file does not import directly |
| S156 | No method for the receiver type, or a field was called |
| S157 | Invalid `method` declaration |
| S158 | Ambiguous method: several imported modules provide it |
| S159 | A matching function exists but is not declared `method` |

Run `tests/extension-methods.ps1 -CompilerDll <Sage.dll>` to validate the runtime
behavior and every diagnostic above.
