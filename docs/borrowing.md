# Borrowing (parameters in v0.9.0, lexical locals in v0.9.1)

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
may only be reborrowed as shared. A call-duration borrow by itself leaves its
source usable after the call; existing lexical loans still apply.
The callee does not acquire a cleanup obligation and cannot delete the borrowed
value. A returned plain value is copied rather than returning its borrow.

## Lexical local borrows (v0.9.1)

An explicit local binding retains a borrow until the end of its lexical scope:

```sage
var owner: Counter = new Counter { value = 1 };
if (true)
{
    var view: &mut Counter = &mut owner;
    view.value++;
    // Direct access to owner is blocked while view is live.
}
owner.value++; // allowed after view's scope ends
```

Shared local borrows allow reads of the source, but block writes, moves, and
deletion. A mutable local borrow blocks direct reads as well. Access through
the owning borrow binding is permitted according to its mode. Loans retain
binding identity, so shadowing a name does not change which value was borrowed.
Local borrow initializers are evaluated before the new C binding is declared.

A reborrow can shorten a parent's access for an inner scope. Shared children
block writes through the parent; mutable children block both reads and writes.
The parent resumes access when those children leave scope. Function calls can
temporarily reborrow local borrow bindings. A `for` initializer's local borrow
ends at the loop's outer scope. These lifetimes are lexical rather than based
on the last use of a binding.

Borrow bindings cannot be rebound to a different source. Assigning through a
mutable binding changes the borrowed value; its initializer must use an explicit
borrow with the same type and mode. A copied/returned plain scalar or supported
struct value is independent of the borrow.

## Remaining lifetime boundaries

Borrows are supported as local initializers, direct call arguments, and function
parameters. They cannot be stored in fields, returned, or declared at the native
ABI boundary. Borrow expressions name a whole local variable or parameter.
Globals, temporaries, and individual field places are rejected in this increment.
Call borrows end at the call boundary. Local scopes and the prohibition on
rebinding, field storage, and borrowed returns prevent a borrow from outliving its source.

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
relationships for field-stored or returned references remain future work.

| Code | Meaning |
|------|---------|
| S170 | Borrow argument missing, wrong type, or wrong access mode |
| S171 | Mutation or mutable reborrow through shared/constant access |
| S172 | Conflicting arguments borrow the same binding |
| S173 | Borrow used outside a supported local/parameter/call contract |
| S174 | Attempt to delete a borrowed value |
| S175 | Borrow target is a temporary or an unsupported field place |
| S176 | Borrow uses a global or an unsupported value type |
| S177 | Another argument has effects that must be evaluated into a local |
| S178 | Access conflicts with a live lexical borrow |

Validation lives in `tests/memory-borrowing.ps1`, `tests/memory-borrow-lifetimes.ps1`,
the Sandbox borrowing programs, and native allocation instrumentation in
`tests/memory-cleanup.ps1`.
