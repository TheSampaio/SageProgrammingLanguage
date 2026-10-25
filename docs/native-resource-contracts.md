# Native resource contracts (v0.17.0)

Native ownership must be declared explicitly instead of inferred from names such
as `open`, `malloc`, or `close`. This increment implements declarations in
the native ABI layer:

```sage
extern native("stdio.h")
{
    type FILE;
    func fclose(value: FILE*): c_int;
    resource OwnedFile: FILE* drop fclose;
}
```

`resource` and `drop` are contextual words. The contract names a Sage facade,
one native pointer representation, and a native destructor in the same alias,
header, and linked library. Declared destructor ABI must accept exactly that
pointer and return `none`, `c_void`, or `c_int`. The actual native implementation
and the correctness of its ABI declaration remain trusted.

Names are unique across the import closure and cannot conflict with structs,
opaque types, functions, or C interop aliases. The initial representation supports
one pointer level to an opaque type or C ABI alias (`none*` is also accepted).
Pointer-to-pointer and unknown pointee layouts are rejected. Native alias, header,
and library must agree with the destructor declaration. Native declarations are
registered before validation, so declaration order and transitive imports work.

The C header exposes a typed private-handle facade before borrowed prototypes,
including when its source declaration appears later. Verbose AST output records the
fully qualified destructor and its declared return type. Contract errors retain
the originating source filename even when detected after import discovery.

## Local ownership

An unsafe conversion of the exact declared native pointer may directly initialize
a local owner. The caller promises that it is either null or a valid, exclusively
owned handle that the declared destructor can consume. This is adoption, not a
checked cast: the compiler cannot prove the pointer's provenance or exclusivity.

```sage
extern native_heap("stdlib.h")
{
    func malloc(size: c_size_t): c_void*;
    func free(value: c_void*): none;
    resource NativeAllocation: c_void* drop free;
}

func main(): none
{
    unsafe
    {
        const first: NativeAllocation = native_heap::malloc(16 as c_size_t) as NativeAllocation;
        var second: NativeAllocation = first; // move, including from const
        // first is unavailable here (S140).
    } // free is called once if the allocation was non-null.
}
```

Initialization from another named owner moves the handle and clears the source.
The analyzer tracks moved/possibly moved bindings across branches and rejects
repeated moves of outer bindings in loops (S140/S142/S145). Overwriting owners
is disabled (S143); assigning to const remains S103. Local bindings and parameters
cannot shadow a resource type name (S150), because the C backend uses its typedef.

Scope exit, early return, and loop iteration exit call typed GCC cleanup helpers
in reverse declaration order. Return expressions are evaluated first. Null and
moved-from handles skip the destructor. Semantic const bindings use internally
mutable bookkeeping so move/drop can clear their handles. Resources use their
declared destructor, never an inferred `free`; transitional `delete` does not
apply to them.

Cleanup is a single best-effort destructor call. Integer status is discarded,
the owner is cleared, and cleanup is not retried or propagated. A resource
contract must therefore promise that the destructor consumes the handle even
when it reports a failure. APIs where failure retains ownership need a different
contract before adoption. A future explicit fallible close/flush API must report
errors before implicit cleanup; this increment provides none. Process termination
(`exit`, `abort`, native crashes) does not unwind scopes.

Inside unsafe, a named owner or borrowed binding can be converted to its exact native pointer for a
native operation. This exposes an alias, without transferring ownership. The
caller must handle null, keep the alias within the owner's lifetime, and must not
release or independently adopt it. Retaining/returning that raw pointer or adopting
it twice can violate these obligations; the unsafe escape is not lifetime checked.
Other resource casts and anonymous adoption are rejected with S192. The facade's
generated handle field is inaccessible through Sage member access.

## Shared and mutable borrows

Resources are non-Copy, but support explicit `&Resource` and `&mut Resource`
parameters and lexical borrowed locals. A borrowed binding never owns the handle
and receives no cleanup helper. Existing lexical loans block moving the owner
until the borrowed scope ends; mutable loans also block competing owner access.
Calls reject shared/mutable conflicts, missing explicit borrows, and effectful
arguments alongside borrows. Reborrowing and const shared access follow the
same rules as other checked borrows; see [borrowing](borrowing.md).

```sage
func is_empty(value: &OwnedFile): b8
{
    unsafe { return (value as FILE*) == (0 as FILE*); }
}

// With a live local owner:
// var view: &OwnedFile = &owner;
// var empty: b8 = is_empty(&view);
```

Moving a resource out of a borrowed binding is rejected (S191), including from
mutable parameters. Deleting a borrow is S174; upgrading shared/const access to
mutable is S171. Whole resource replacement remains disabled, even through a
mutable borrow. Borrowed returns and native ABI borrow signatures remain disabled
(S173); resource arrays/fields are still unsupported.

Raw handle exposure inside a borrowed wrapper is trusted unsafe code. Its author
must respect shared versus mutable access, check required handle validity, and
must not release, retain, or independently adopt the handle. The compiler checks
the Sage loan graph, not mutations or aliases created inside native code.

## Owning returns

A Sage function returning a resource transfers ownership of a named local owner.
The source handle is cleared before its scope cleanup, while other locals retain
normal reverse-order destruction. A const owner may also be returned. Returning
through a live lexical loan is rejected, and returning a borrowed binding as an
owner is S191. The entry point cannot return a resource.

```sage
func empty_file(): OwnedFile
{
    unsafe
    {
        const owner: OwnedFile = (0 as FILE*) as OwnedFile;
        return owner;
    }
}

func main(): none
{
    var owner: OwnedFile = empty_file(); // adopts the returned ownership
} // non-null returned handles would be destroyed here
```

An owning call result must directly initialize a local owner. Discarding it,
projecting/casting an anonymous result, or writing `return factory();` is S193.
Bind the result to a local before further use or return. This bounded producer
rule keeps every cleanup obligation explicit; it does not implement general
temporary destruction. Declaring a resource return type does not make a function
unsafe: a reviewed factory can isolate acquisition inside a local unsafe block.
Raw validity, exclusive acquisition, and native destructor behavior remain its
trusted obligations. Native ABI resource returns remain disabled.

## Consuming parameters

A resource parameter by value consumes a named local owner or owning parameter.
The caller is cleared and becomes unavailable; the callee adopts cleanup before
executing its body. Borrowed bindings cannot be consumed (S191), and live lexical
loans block consuming the original owner (S178). Passing the same owner twice is
S140. Consuming an outer binding repeatedly in a loop remains conservatively S145.

```sage
func consume(value: OwnedFile): none {} // implicit cleanup even if unused

func identity(value: OwnedFile): OwnedFile { return value; }

func main(): none
{
    var first: OwnedFile = empty_file();
    var second: OwnedFile = identity(first);
    consume(second);
    // first and second are now unavailable.
}
```

S194 requires consuming arguments to be named owners: bind an acquisition or
factory result before calling. A call cannot consume and borrow the same resource,
in either argument order. Other arguments must use the existing supported pure
argument subset; bind calls, mutations, and unsupported aggregate expressions
first. These restrictions keep move operations independent of C's unspecified
argument evaluation order. Separate resources may be consumed/borrowed together.
Raw aliases inside unsafe remain subject to their existing lifetime obligations.

The C backend transfers each argument through a typed GNU statement expression
and clears its caller binding. The callee stores incoming resource carriers in
cleanup locals in parameter declaration order. Body locals are destroyed first,
then owned parameters in reverse order. Moving/returning an owning parameter
clears it before cleanup, so it does not destroy the transferred handle again.
Entry points cannot accept resource owners or borrows. Native ABI resource
parameters remain disabled; use reviewed unsafe wrappers over native pointers.

## Safe presence queries

`owner == null` and `owner != null` inspect whether a named resource has an empty
handle. The symmetric forms are also supported. Shared and mutable borrowed
bindings can be checked with the same syntax, without unsafe or ownership transfer:

```sage
func is_empty(value: &OwnedFile): b8 { return value == null; }
```

Availability and loan checks still apply: checking a moved owner is S140, and
direct owner access during a mutable loan is S178. The result is an ordinary
Copy boolean; it does not refine the resource's type or prove a native handle's
validity. Scope cleanup and ownership obligations are unchanged.

`null` is contextual in these binary comparisons when no binding with that name
exists. Existing variables named `null` keep their normal meaning. It is not a
general value, resource constructor, or raw-pointer null literal. Only `==` and
`!=` with one contextual null and one named resource are supported; invalid
comparisons use S195. Resource-to-resource comparison remains disabled. Producer
results still require direct local ownership (S193) before a query.

Presence queries used as other arguments of a consuming call must first be
evaluated into a local boolean (S194). This conservative rule also applies to
nested queries and separate resources, preventing dependence on whether a C
argument moves/clears an owner before the query is evaluated. Borrow-only calls
may receive pure presence expressions directly.

## Remaining boundaries

Resource globals, fields, arrays, and pointers
to facade types remain rejected with S191. Other resource comparisons,
interpolation, and implicit native arguments are also disabled. A resource is
not inferred Copy. The bounded [byte-file API](byte-files.md) now provides safe
borrowed byte operations and consuming close after valid acquisition. General
path acquisition and owned string/buffer abstractions remain unfinished.

Implementation order:

1. Implemented: parse and validate declarations after the complete import closure is known.
   Preserve native origin and diagnostic source file, and emit a typed facade.
2. Implemented: exclusive local ownership, moves, and single-call nullable-handle
   cleanup after trusted unsafe acquisition. Shared/mutable resource borrows reuse
   call conflict and lexical loan checks without transferring ownership.
3. Implemented: owning returns from named locals into direct owning call initializers.
4. Implemented: consuming named resource parameters with callee cleanup and
   ownership/borrow call conflicts.
5. Implemented: safe contextual null comparisons on named owners and borrows.
6. Planned: add a bounded standard-library interface. Nested resource fields,
   arrays, buffers, and C++ adapters need their
   own subsequent contracts.

Invalid native contracts use S190. Acceptance tests are in
`tests/native-resource-contracts.ps1`: forward
declarations, context-sensitive words, native ABI/origin consistency, collisions,
unsupported operations, and imported diagnostics. `tests/native-resource-ownership.ps1`
adds native instrumentation for liveness, invalid/double release, moves, const,
shadowing, early returns, destruction order, null handles, loops, void destructors,
and integer failure status, plus semantic rejection cases and a minimal-header
dependency check. Sandbox tests exercise the standard C allocator integration;
the native instrumented fixture is the evidence for cleanup counts and order.
`tests/native-resource-borrowing.ps1` covers positive borrowed calls/locals,
reborrowing, forward prototypes, and rejection of illegal moves, alias conflicts,
borrow escapes, native ABI borrows, and unsupported resource compositions.
`tests/native-resource-returns.ps1` covers factories, forwarding through locals,
branches, imports/modules, and rejection of unbound producer results, invalid
returns, active-loan moves, and missing return paths. Native instrumentation
verifies returned liveness, identity, final cleanup, and cleanup of other owners.
`tests/native-resource-parameters.ps1` covers consuming locals/parameters,
returning and forwarding them, cleanup header dependencies, imports/modules,
call conflicts, branch availability, and entry-point/native ABI restrictions.
Native instrumentation checks unused parameters, early returns, forwarding,
identity returns, mixed ownership/borrowing, and local/parameter cleanup order.
`tests/native-resource-presence.ps1` covers safe/symmetric/borrowed queries,
contextual name shadowing, move/loan conflicts, consuming-argument restrictions,
the generated NULL header dependency, and the semantic AST marker. Native runtime
checks verify live and empty handles, no ownership transfer, and final cleanup.
