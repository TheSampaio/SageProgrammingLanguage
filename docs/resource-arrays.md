# Local fixed arrays of native resources (v0.27.0)

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

This is a bounded contract. Whole-array copies and assignments, array
parameters/returns, globals, borrows, resource-array fields, and moving or
replacing individual elements are rejected. Arrays of structs that contain
resources also remain unsupported. Unsafe native code can still violate the
declared ownership contract. `tests/native-resource-array.ps1` checks cleanup,
partial initialization, source invalidation, and rejected unsupported uses.
