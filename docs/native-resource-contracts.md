# Native resource contract declarations (v0.12.0)

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
pointer and return `none`, `c_void`, or `c_int`. An integer destructor result will
require an explicit cleanup error policy in the operational API; this declaration
does not provide one. The actual native implementation remains trusted.

Names are unique across the import closure and cannot conflict with structs,
opaque types, functions, or C interop aliases. The initial representation supports
one pointer level to an opaque type or C ABI alias (`none*` is also accepted).
Pointer-to-pointer and unknown pointee layouts are rejected. Native alias, header,
and library must agree with the destructor declaration. Native declarations are
registered before validation, so declaration order and transitive imports work.

The C header exposes a typed private-handle facade. Verbose AST output records the
fully qualified destructor and its declared return type. Contract errors retain
the originating source filename even when detected after import discovery.

Implementation order:

1. Implemented: parse and validate declarations after the complete import closure is known.
   Preserve native origin and diagnostic source file. Emit a typed facade, but
   reject resource value operations until ownership lowering exists.
2. Planned: add exclusive local ownership, moves, shared/mutable borrows, and exactly-once
   nullable-handle cleanup. Trusted acquisition must establish exclusive ownership.
3. Planned: add ownership transfer through parameters/returns and a bounded standard-library
   interface. Nested resource fields, arrays, buffers, and C++ adapters need their
   own subsequent contracts.

This declaration foundation does not implement automatic cleanup or a safe file
API. Resource values in locals, signatures, fields, arrays, or
casts remain rejected with `S191` during that first increment. Invalid native
contracts use `S190`; conflicting type names retain `S150`.

Acceptance tests are in `tests/native-resource-contracts.ps1`: forward
declarations, context-sensitive words, native ABI/origin consistency, collisions,
unsupported operations, and imported diagnostics. Later increments must add
native destruction instrumentation before enabling value construction.
