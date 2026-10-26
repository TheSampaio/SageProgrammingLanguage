# Local structs with native resources (v0.25.0)

A struct may declare direct fields of a validated native resource type. A local
literal may initialize each such field from a named owning local or parameter.
Each source moves into the struct and becomes unavailable (S140). Missing fields
are zero-initialized. At scope exit, the compiler calls the declared resource
destructors in reverse field declaration order, skipping null handles. Early
returns and loop iteration exits use the same GCC cleanup mechanism as local
resource owners.

```sage
struct Pair { first: OwnedHandle; second: OwnedHandle; }
var first: OwnedHandle = produce(1);
var second: OwnedHandle = produce(2);
var pair: Pair = { first = first, second = second };
// first and second are moved; pair owns both handles.
```

This is a bounded contract. The struct must be declared in the same Sage source
as the local binding. Whole-struct copies, assignments, borrowing, function
parameters/returns, globals, arrays, pointers to these structs, nested resource structs, and heap `new`
are rejected with S196. Replacing or extracting a resource field is not yet
supported. Copyable scalar fields remain accessible and mutable. Unsafe native
calls and fabricated handles can still violate the declared destructor contract.

The subset allows partial field initialization because omitted resource fields
are null; it does not implement general uninitialized fields or partial moves
out of a struct. `tests/native-resource-struct.ps1` covers native liveness,
destruction order, early return, empty structs, and rejected unsupported uses.
