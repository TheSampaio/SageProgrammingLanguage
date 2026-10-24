# Borrowed parameters (v0.9.0)

Borrowing grants temporary access to a value without transferring its ownership:

```sage
struct Counter { value: i32; }

func read(counter: &Counter): i32 { return counter.value; }
func increment(counter: &mut Counter): none { counter.value++; }

func main(): none
{
    var counter: Counter = new Counter { value = 1 };
    var before: i32 = read(&counter);
    increment(&mut counter);
    // counter is still owned here and has value 2.
}
```

`&T` grants shared read access; `&mut T` grants exclusive mutable access. The
argument must spell the same contract explicitly. `mut` is contextual after `&`
and remains a valid ordinary identifier elsewhere. This is a borrow operator,
with no pointer arithmetic or exposed address value in safe Sage.

Multiple shared arguments can refer to the same binding in one call. A mutable
argument conflicts with any other borrow of that binding in the same call.
Mutable borrows of different bindings are allowed. Constants and shared
parameters cannot be mutably borrowed. Assignments and increments through a
shared parameter are rejected, including mutations of nested fields.

A mutable parameter may be reborrowed as shared or mutable; a shared parameter
may only be reborrowed as shared. The source owner remains usable after the call.
The callee does not acquire a cleanup obligation and cannot delete the borrowed
value. A returned plain value is copied rather than returning its borrow.

## Current lifetime boundary

Borrows are supported only as direct call arguments and function parameters.
They cannot be stored in locals or fields, returned, or declared at the native
ABI boundary. Borrow expressions name a whole local variable or parameter.
Globals, temporaries, and individual field places are rejected in this increment.
These restrictions make the lifetime end at the call boundary and prevent a
safe callee from retaining the reference.

Supported value types are numeric scalars, booleans, and structs recursively
composed of these types. This property is inferred from field composition.
Strings, arrays, raw pointers, opaque resources, and recursive value layouts
require further contracts and are rejected. In particular, a struct with a
`str` field is outside this initial supported subset.

When a call has borrowed parameters, its other arguments must be value
expressions without calls, assignments, or increments. Evaluate such operations
into locals first. This avoids depending on C's argument evaluation order during
the initial implementation. Borrow modes do not silently coerce at call sites:
use `&value` for a shared parameter and `&mut value` for a mutable one.

The C ABI represents these parameters as typed pointers (`const T*` for shared,
`T*` for mutable), with no heap allocation for scalar or stack borrows. Those
pointers are compiler implementation details. Existing trusted native calls and
`unsafe` casts can bypass the checked model; general FFI safety and lifetime
relationships for stored or returned references remain future work.

| Code | Meaning |
|------|---------|
| S170 | Borrow argument missing, wrong type, or wrong access mode |
| S171 | Mutation or mutable reborrow through shared/constant access |
| S172 | Conflicting arguments borrow the same binding |
| S173 | Borrow used outside a supported parameter/call contract |
| S174 | Attempt to delete a borrowed value |
| S175 | Borrow target is a temporary or an unsupported field place |
| S176 | Borrow uses a global or an unsupported value type |
| S177 | Another argument has effects that must be evaluated into a local |

Validation lives in `tests/memory-borrowing.ps1`, the Sandbox `borrowing.test.sg`
program, and the native allocation instrumentation in `tests/memory-cleanup.ps1`.
