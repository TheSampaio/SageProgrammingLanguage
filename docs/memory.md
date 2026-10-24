# Implemented memory semantics

## Local heap references (v0.8.2)

`new` and `delete` remain transitional syntax. A local binding initialized from
`new` owns that allocation. Initializing another binding from it transfers
ownership instead of making an independently usable alias:

```sage
var first: Item = new Item { value = 7 };
var second: Item = first;
// first.value is now an S140 error.
second.value = 8;
delete second;
// second.value and another delete are now S141 errors.
```

Generated C transfers the pointer and clears the source. Ordinary struct values
without `new` retain their existing C value-copy behavior. Field access on a live
reference does not transfer ownership.

Branch analysis restores the incoming state before checking each alternative.
A binding is usable after a join only when it is available on every continuing
path. Returning branches do not constrain the remaining path. The compiler does
not assume that a condition is true merely because it is a boolean literal.

Consuming a reference declared outside a loop is conservatively rejected. A
reference declared inside the current iteration may be transferred or deleted
there. This prevents a later iteration from consuming the same binding again;
more precise loop analysis remains future work.

## Diagnostics

| Code | Meaning |
|------|---------|
| S140 | Use of a binding after ownership transfer |
| S141 | Use of a binding after deletion, including double deletion |
| S142 | Binding may be unavailable on another control-flow path |
| S143 | Whole-reference reassignment is not supported yet |
| S144 | Operation needs an owning or borrowing contract not yet implemented |
| S145 | A loop might consume an outer reference repeatedly |

S140–S142 report the source location of the invalidating operation. Passing or
returning heap references, and storing them inside arrays or struct fields, is
rejected until those operations carry valid ownership contracts. Raw pointer
casts still require `unsafe` and leave the checked reference model.

## Boundaries

This increment does not implement automatic cleanup, borrows, lifetime analysis,
owned strings or buffers, or ownership-aware native calls. An allocation can
still leak when no `delete` executes. Native aliases created through `unsafe`
remain the programmer's responsibility. The parser already requires non-array
variables to have initializers (`S012`); arrays retain zero initialization.

The [target model](memory-model-proposal.md) describes subsequent work. Validate
the current subset with `tests/memory-ownership.ps1 -CompilerDll <Sage.dll>` and
the Sandbox `ownership.test.sg` program.
