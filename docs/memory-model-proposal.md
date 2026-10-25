# Proposed ownership model (design, not implemented)

This document records the target contract for the next compiler increments.
The [implemented subset](memory.md) covers local reference transfers, cleanup,
and invalidation after deletion. [Borrowed parameters](borrowing.md) implement
`&T` and `&mut T` for the duration of a call; lexical local bindings are also
implemented. Field-stored and returned borrows remain proposed work.
[Value-copy capabilities and fixed-array copies](value-copy.md) are implemented
for a bounded subset. [Native resource contracts](native-resource-contracts.md)
now cover local ownership, borrows, owning callables, and safe presence queries.
Owned buffers/strings, safe file operations, aggregate resource ownership, and
the broader contracts below remain proposed work.
Existing `new`, `delete`, and `unsafe` programs remain valid until a separately
tested migration deliberately changes that behavior.

## Values and capabilities

- Fixed-width numeric values, booleans, and character values are `Copy`.
- A struct is `Copy` only when every field is `Copy`. Arrays follow the same
  rule for their element type. Copy is a value copy and never duplicates a
  resource handle.
- Owned buffers, open files, and native handles are non-`Copy`. Assignment,
  argument passing to an owning parameter, and return transfer them by move.
  Any use of the former owner after the move is a compile-time error.
- `str` needs an explicit representation decision before migration. Its current
  `char*` mapping cannot safely imply either ownership or a static lifetime.

## Borrows

The candidate contract forms are `&T` for shared access and `&mut T` for
exclusive mutable access. The corresponding expression forms are `&value` and
`&mut value`. These are references to a value with no address arithmetic in
safe Sage. A function signature must say whether it borrows or takes ownership.
The compiler should permit multiple shared borrows or one mutable borrow of a
place, but never both simultaneously. A borrowed value cannot outlive its owner
or be moved or destroyed while the borrow is active. This requires analysis of
control-flow joins, not just a mutable flag in `SymbolMetadata`.

## Destruction

An owned non-`Copy` value is destroyed exactly once at the end of its lifetime:
normal block exit, early return, loop exit, and error path. A move transfers the
destructor obligation. Nested structs destroy owned fields; partially initialized
values destroy only initialized fields. Generated C may use pointers and cleanup
helpers, but safe Sage source must not need `new`, `delete`, or raw pointers.
The compiler must choose whether assignment to an initialized owner first
destroys the old value or is rejected. This decision needs an executable test.

## FFI boundary

The native ABI layer may contain `T*`, opaque handles, and C-compatible calls.
The safe layer must state the acquisition and release pair, nullability, buffer
length, encoding, ownership transfer, and any borrowed lifetime. Unknown native
contracts stay unsafe; a wrapper is not safe merely because it hides a pointer.

## Acceptance cases for the first implementation steps

- Copyable primitive and recursively copyable struct assignments retain both
  usable values.
- Moving an owned buffer invalidates its old binding; attempting to read,
  move, or destroy that binding is rejected with a stable diagnostic code.
- Shared borrows coexist. A mutable borrow conflicts with any other live
  borrow; mutation or move of the owner while borrowed is rejected.
- Returning a borrow of a local value is rejected. Borrowed parameters remain
  valid only within their declared lifetime contract.
- A file or buffer released on every scope exit, including early returns and
  branches, passes runtime tests and native sanitizer checks where supported.
- Safe source containing `new`, `delete`, or raw pointer syntax is rejected only
  after the standard library and Sandbox have migrated to equivalent APIs.

The first code increment should make type capabilities explicit in semantic
analysis and add negative tests before enabling move behavior. Later stages
must validate generated C behavior; passing semantic tests alone is insufficient.
