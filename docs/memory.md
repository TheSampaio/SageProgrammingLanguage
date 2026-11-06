# Implemented memory semantics

## Breaking change: `new` / `delete` require `unsafe` (v0.32.2)

Safe Sage no longer exposes manual memory management. `new` and `delete` are
rejected outside an `unsafe { }` block or `unsafe func` with **S133**:

```text
main.sg(5,24): error S133: 'new' is manual memory management and requires an
'unsafe' block or 'unsafe func'. Use a plain struct value instead; it is
cleaned up automatically.
```

Raw pointers were already unsafe-only (S120-S123). Inside `unsafe`, the legacy
heap references below keep every existing check (moves S140, deletion S141,
branch joins S142, direct owners S146, scope cleanup), so `unsafe` code that
needs a stable address for C, such as `std/http_server.sg`, keeps working.

### Migration

| Before (safe code) | After |
|--------------------|-------|
| `var a: T = new T { f = 1 };` | `var a: T = { f = 1 };` (a plain value) |
| `delete a;` | Remove it; values and owners are released at scope exit. |
| Passing the reference to mutate it | Pass `&mut a` to a `&mut T` parameter. |
| Reading through a shared reference | Pass `&a` to a `&T` parameter. |
| Transferring a native handle | Use a `resource` owner or a resource struct; they move. |
| Handing C a stable address | Keep `new` inside `unsafe` next to the native call. |

Plain struct values copy when all fields are Copy, and move when they own
native resources ([resource structs](resource-structs.md)). Programs that relied
on a heap reference only to avoid a copy should use a borrow instead.

## Local heap references (v0.8.2, unsafe-only since v0.32.2)

`new` and `delete` are a trusted unsafe boundary. A local binding initialized from
`new` owns that allocation. Initializing another binding from it transfers
ownership instead of making an independently usable alias:

```sage
// Inside unsafe { ... }:
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
| S133 | `new` or `delete` used outside an unsafe context |

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

### Direct allocation binding (v0.10.1)

Every `new` expression must directly initialize a local owning binding, including
inside `unsafe` code. Parentheses around that initializer are allowed. Creating an
anonymous allocation and projecting a field from it is rejected with `S146`;
the same rule covers nested allocations in arguments, fields, casts, comparisons,
and array elements. A surrounding owning initializer does not adopt inner
allocations. Bind the owner before reading its fields:

```sage
// Inside unsafe { ... }. Rejected: var value: i32 = (new Item { value = 7 }).value;
var owner: Item = new Item { value = 7 };
var value: i32 = owner.value;
```

This closes a confirmed leak where an anonymous field projection had no cleanup
binding. Returning a plain field value from a named local owner remains valid:
the value is evaluated before the owner's scope cleanup. A raw pointer cast of a
named owner still belongs to the existing trusted `unsafe` boundary; it does not
extend the owner's lifetime. Validate allocation contexts with
`tests/allocation-contexts.ps1` and native cleanup instrumentation.

## Guarantees and limits (v0.32.2)

**Proven statically for safe Sage** (code outside `unsafe` and `extern`), before
any C is generated:

- No manual allocation or release: `new`, `delete`, raw pointer types, pointer
  casts, and pointer indexing are rejected (S133, S120-S123).
- Copy vs. move is inferred from composition. Scalars and structs of Copy
  fields copy. Native resources and same-file resource structs move; the source
  is unavailable afterwards (S140), including after a branch that may have moved
  it (S142) or a repeated loop path (S145).
- Every owned native handle has exactly one owner. Cleanup runs once at scope
  exit, early return, and loop iteration exit, in reverse declaration order and
  recursively through struct fields and arrays. Moved-from, empty, and replaced
  places release nothing twice; replacement releases the old value once.
- Borrows (`&T`, `&mut T`) cannot outlive their owner: they cannot be returned,
  stored in fields or arrays, rebound, fabricated by casts, or taken of globals,
  fields, or temporaries. While a lexical borrow lives, the owner cannot be
  moved, consumed, replaced, or written (S178); a mutable borrow also blocks
  reads. One call cannot both consume and borrow an owner (S194).
- Fixed-array indexing is checked: constant out-of-range indices are S185 and
  dynamic ones stop the program with runtime S186 before any access.

**Trusted, not proven** (the programmer's responsibility):

- Everything inside `unsafe`: legacy `new`/`delete` references (still move- and
  double-delete-checked), raw pointers, handle adoption (`ptr as Resource`),
  and raw handle exposure from owners or borrows.
- Native declarations: the ABI signature, the declared destructor's behavior,
  and what native code does with any pointer it receives.
- `str` is a C `char*` without an ownership or lifetime contract. Interpolation no
  longer uses shared buffers: `$"..."` builds an owned `Text` ([strings](strings.md)).
- Process termination (`exit`, `abort`, native crashes, runtime S186) does not
  run scope cleanup. Allocation failure inside `new` aborts.
- Not yet supported (rejected rather than unchecked): borrows of resource
  structs, field or element borrows, moving a field or element out of an
  aggregate, whole resource-array transfers, imported resource-struct layouts
  by value, owned strings and growable collections.

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
