# Local structs with native resources (v0.25.0-v0.32.0)

A struct may declare direct fields of a validated native resource type. A local
literal may initialize each such field from a named owning local or parameter.
Each source moves into the struct and becomes unavailable (S140). Missing fields
are zero-initialized. At scope exit, the compiler calls the declared resource
destructors in reverse field declaration order, skipping null handles. Early
returns and loop iteration exits use the same GCC cleanup mechanism as local
resource owners.

Since v0.26.0, a local struct may contain another local resource struct as a
direct field. Declare the inner type earlier in the same source file and
initialize that field with a nested literal. Cleanup recursively visits fields
in reverse declaration order. Missing nested fields are zero-initialized.

```sage
struct Pair { first: OwnedHandle; second: OwnedHandle; }
struct Wrapped { pair: Pair; }

func example(): none
{
    var first: OwnedHandle = produce(1);
    var second: OwnedHandle = produce(2);
    var wrapped: Wrapped = { pair = { first = first, second = second } };
    // first and second are moved; wrapped owns both handles.
}
```

This is a bounded contract. The struct must be declared in the same Sage source
as the local binding. Borrowing, globals, pointers to these structs, native ABI
signatures, and heap `new` are rejected with S196. Extracting a resource field
is not yet supported. Copyable scalar fields remain accessible and mutable.
Unsafe native calls and fabricated handles can still violate the declared
destructor contract.

## Whole-struct moves (v0.32.0)

A resource struct is a non-Copy value: every whole-value transfer is a move.
The source binding becomes unavailable (S140, or S142 after a branch) and its
storage is zeroed, so its own scope cleanup releases nothing. Each handle is
therefore released exactly once, by its final owner. Supported transfers:

- `var b: Pair = a;` moves a named local owner or owning parameter.
- `var b: Pair = build();` receives a direct owning call result.
- `b = a;` and `b = { first = owner };` are standalone replacements: the
  incoming value is taken first, then the old destination is released
  recursively, then the new value is stored. `b = {}` releases and empties it.
- `{ nested = inner }` and `{ first_item, second_item }` move named owners into
  a nested field or a resource-struct array element.
- `func take(pair: Pair)` adopts the argument; the callee releases it at its
  scope exit unless it moves it onward. The argument must name a local owner.
- `func build(): Pair` returns a named local owner or a struct literal.

```sage
func build(id: i32): Pair
{
    var handle: OwnedHandle = produce(id);
    return { first = handle, value = id };
}

func take(pair: Pair): i32 { return pair.value; } // pair is released here.

func example(): none
{
    var a: Pair = build(1);
    var b: Pair = a;          // a is moved and unavailable.
    var total: i32 = take(b); // b is moved into the callee.
}
```

The same restrictions as owning resources apply: self-assignment, replacement
inside a larger expression, and replacement from an anonymous call result are
S143; a discarded or projected owning call result is S193; a call or literal
passed to an owning parameter is S194; moving an outer owner inside a loop is
S145. Constant destinations remain S103, but a `const` owner can be moved.
The entry point cannot return a resource struct (S191). Whole-array copies and
assignments, resource-struct borrows (`&Pair`), and imported struct layouts
stay outside the contract (S196).

Since v0.29.0, a standalone assignment may replace a direct native resource
field of a mutable local resource struct from a named local owner. The old field
is released once, the source becomes unavailable, and the replacement is cleaned
up with the struct. An empty old field requires no destructor call. Anonymous
producer results, borrowed or already moved sources, conflicting loans, and
constant destinations are rejected. Since v0.30.0, the field may be reached
through nested resource structs rooted at the same local binding. Cleanup still
visits all fields recursively. Resource fields of array elements remain outside
this replacement contract until v0.32.0.

Since v0.32.0, the replaced place may be any path of fields and checked array
elements rooted at a local resource struct, resource array, or resource-struct
array: `items[i].owner = next`, `items[i] = pair`, `handles[i] = next`, and
`outer.nested = inner`. The destination place is evaluated once, after the
incoming value is taken; an out-of-range index stops with runtime S186 before
anything is released.

```sage
var pair: Pair = {};
var next: OwnedHandle = produce(3);
pair.first = next; // next moves; the previous pair.first is released.

var wrapped: Wrapped = {};
var another: OwnedHandle = produce(4);
wrapped.pair.first = another; // A nested field path also moves the owner.
```

Since v0.28.0, local fixed arrays of these structs support direct element
literals and recursive cleanup. See [resource arrays](resource-arrays.md).

Nested resource fields accept a literal or, since v0.32.0, a named inner owner
move. Forward references to resource-containing inner layouts remain
unsupported. `tests/native-resource-nested-struct.ps1` checks recursive cleanup
and these rejection boundaries; `tests/native-resource-struct-move.ps1` checks
whole-struct moves, parameters, returns, and array-element replacement.

The subset allows partial field initialization because omitted resource fields
are null; it does not implement general uninitialized fields or partial moves
out of a struct. `tests/native-resource-struct.ps1` covers native liveness,
destruction order, early return, empty structs, and rejected unsupported uses.
