# Checked value copies (v0.10.0)

`TypeCapabilities` infers the checked copy subset from composition: numeric and
boolean scalars, structs whose fields are copyable, and fixed arrays whose
elements are copyable. No annotation is required. This does not infer ownership
for native handles stored in numeric ABI aliases. Strings, raw pointers, opaque
types, borrowed types, and recursive value layouts have no checked copy contract.
Legacy uses of these types outside this subset are not made safe by this increment.

Fixed-array local initialization, whole-array assignment, and Sage value parameters
now copy elements into independent storage. Copying a struct with array fields
already uses the C struct value copy. Neither operation transfers ownership or
invalidates the source, and neither implicitly duplicates an owned resource.

```sage
func change(values: i32[2]): none { values[0] = 9; }
var first: i32[2] = { 1, 2 };
var second: i32[2] = first;
change(first); // first[0] remains 1.
second = first;
second = { 3 }; // second[1] is zero.
```

Array parameters previously decayed to C pointers and could mutate the caller.
They now copy on function entry, before the Sage body runs. The C entry point
reads a const-qualified input pointer into a local array. Native `extern` array
parameters preserve their original ABI and trusted aliasing behavior. Evaluation
order among other call arguments remains C's order; this does not add a general
left-to-right argument snapshot contract.

Array copies use stack storage and `memmove`; their cost is proportional to the
array's byte size. Whole-array assignment evaluates its destination first and
source second, once each, and supports self-assignment. Const copies use hidden
stack storage exposed through a read-only compiler binding. Mutable copies use
the corresponding writable binding. Shadowing evaluates
the source before declaring the new binding. No heap allocation is added.

Structs containing copyable fixed arrays now support `&T` and `&mut T`. Shared
access rejects array-field mutation; passing an array field to a Sage value
parameter copies it, preserving the shared source. Direct `&i32[N]` contracts
remain unsupported. Existing lexical loans and fixed-array bounds checks apply.

`S188` identifies unsupported array copy contexts: noncopyable element layouts,
array returns, global copy initialization/assignment, and array-field initialization
from an existing array. For the last case, initialize the containing struct and
then assign its array field. Array literals remain accepted in fields, Sage
arguments, and assignment. Existing legacy string-array literals remain available;
this does not permit checked copies of their unchecked storage.

Array-field reads from returned temporaries can be copied as a whole while the
temporary is alive. The backend consumes the source inside the copying call's
full expression, following [C11 draft N1570, 6.2.4 paragraph 8](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf).
Indexing or mutating such a temporary field is rejected with `S189`: bind the
containing struct to a local first. This also prevents the checked-index lowering
from retaining an element pointer after its temporary source has expired.

Validation: `tests/array-copy.ps1` covers positive/negative compilation and a
cross-module value parameter; `Sandbox/tests/array_copy.test.sg` verifies copy
independence, constants, shadowing, nested structs, borrows, and evaluation count.
