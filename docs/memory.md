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
| S146 | An allocation needs a local owning binding |

S140–S142 report the source location of the invalidating operation. Passing or
returning heap references, and storing them inside arrays or struct fields, is
rejected until those operations carry valid ownership contracts. Raw pointer
casts still require `unsafe` and leave the checked reference model.

## Automatic local cleanup (v0.8.3)

Local owning references are released automatically at scope exit, including
early returns and the end of a loop iteration. Destruction occurs in reverse
declaration order. A return expression is evaluated before cleanup. Moves clear
the source pointer, and a transitional explicit `delete` also clears its binding;
subsequent scope cleanup therefore does not release the allocation twice.

The C backend uses typed cleanup helpers and GCC's `cleanup` variable attribute.
GCC is already the supported native toolchain and GNU statement expressions were
already required by allocation and interpolation. Other native C compilers have
not been validated. Allocation failure calls `abort()` before dereferencing a
null pointer; recoverable allocation errors remain future work. Process-level
termination (`exit`, `abort`, native failures) does not unwind Sage scopes.

Anonymous allocation statements and global heap allocations are rejected with
`S146`: every checked allocation needs a local owner. This keeps the compiler's
cleanup obligation explicit. `tests/memory-cleanup.ps1` instruments native
allocation and release to check leaks, double release, return values, and order.

## Remaining boundaries

[Borrowed parameters and lexical locals](borrowing.md) now support shared and
mutable access for a bounded set of value types. Field-stored/returned reference lifetimes,
owned strings or buffers, and ownership-aware native calls remain future work.
Native aliases created through `unsafe`
remain the programmer's responsibility. The parser already requires non-array
variables to have initializers (`S012`); arrays retain zero initialization.

[Copyable value composition and fixed-array value copies](value-copy.md) are
implemented separately in v0.10.0. They do not infer ownership for legacy strings
or native resource representations.

The [target model](memory-model-proposal.md) describes subsequent work. Validate
the current subset with `tests/memory-ownership.ps1 -CompilerDll <Sage.dll>` and
the Sandbox `ownership.test.sg` program.
