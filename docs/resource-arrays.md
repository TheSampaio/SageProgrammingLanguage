# Local fixed arrays of native resources (v0.27.0-v0.28.0)

A local fixed array may contain direct native resource owners. A literal moves
each named local owner or parameter into its corresponding element. Sources
become unavailable (S140); the compiler rejects duplicate sources and moves
during live conflicting loans. Omitted positions and arrays without an
initializer are zero-initialized.

```sage
func example(): none
{
    var first: OwnedHandle = produce(1);
    var second: OwnedHandle = produce(2);
    var items: OwnedHandle[3] = { first, second };
    // first and second are moved; items[2] is empty.
} // Non-null elements are released in reverse index order.
```

Generated C uses a typed GCC cleanup helper for the complete array. It visits
elements from the highest index down to zero on ordinary scope exit, early
return, and loop iteration exit, and skips null handles. No array heap
allocation is added. The native resource destructor retains its declared
best-effort contract.

Since v0.28.0, a local fixed array may also contain a same-file struct with
direct or nested resource fields. Each initialized element must be a struct
literal. Omitted elements and fields are zeroed. Cleanup visits elements in
reverse index order and recursively visits resource fields in reverse declaration
order. A local `const` array has the same cleanup behavior.

```sage
struct Item { owner: OwnedHandle; }

func example_struct_array(): none
{
    var owner: OwnedHandle = produce(1);
    var items: Item[2] = { { owner = owner } };
    // owner is moved; items[1] is empty.
}
```

Since v0.32.0, a named resource struct owner can be moved into an element
literal, and a standalone assignment can replace an element or a resource field
reached through an element: `handles[i] = next`, `items[i] = pair`,
`items[i] = {}`, or `items[i].inner.owner = next`. The source must be a named
local owner (or a literal for a struct element). The old value is released once
after the incoming value is taken; the index is checked first.

This is a bounded contract. Whole-array copies and assignments, array
parameters/returns, globals, borrows, resource-array fields, and moving an
element out of an array are rejected. Unsafe native code can still violate the
declared ownership contract. `tests/native-resource-array.ps1`,
`tests/native-resource-struct-array.ps1`, and `tests/native-resource-struct-move.ps1`
check cleanup, partial initialization, source invalidation, replacement, and
rejected unsupported uses.
