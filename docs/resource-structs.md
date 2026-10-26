# Local structs with native resources (v0.25.0-v0.30.0)

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
as the local binding. Whole-struct copies, assignments, borrowing, function
parameters/returns, globals, pointers to these structs, and heap `new`
are rejected with S196. Extracting a resource field is not yet supported.
Copyable scalar fields remain accessible and mutable. Unsafe native
calls and fabricated handles can still violate the declared destructor contract.

Since v0.29.0, a standalone assignment may replace a direct native resource
field of a mutable local resource struct from a named local owner. The old field
is released once, the source becomes unavailable, and the replacement is cleaned
up with the struct. An empty old field requires no destructor call. Anonymous
producer results, borrowed or already moved sources, conflicting loans, and
constant destinations are rejected. Since v0.30.0, the field may be reached
through nested resource structs rooted at the same local binding. Cleanup still
visits all fields recursively. Resource fields of array elements remain outside
this replacement contract.

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

Nested resource fields must use literals. Moving a previously bound inner
struct into an outer field remains unsupported, as do forward references to
resource-containing inner layouts. `tests/native-resource-nested-struct.ps1`
checks recursive cleanup and these rejection boundaries.

The subset allows partial field initialization because omitted resource fields
are null; it does not implement general uninitialized fields or partial moves
out of a struct. `tests/native-resource-struct.ps1` covers native liveness,
destruction order, early return, empty structs, and rejected unsupported uses.
