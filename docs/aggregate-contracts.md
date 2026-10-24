# Aggregate initialization contracts (v0.9.2)

Struct and fixed-array literals are checked against their destination type before
C generation. The same rules apply to local initialization, assignment, Sage
arguments, return expressions, and fields of transitional `new` allocations.
Nested literals inherit the corresponding field or element type.

```sage
struct Item { value: i32; }
struct Pair { left: Item; right: Item; }
var pair: Pair = { left = { value = 3 } };
// pair.right.value is zero; ordinary nested structs retain value-copy behavior.
var items: Item[2] = { { value = 4 } };
```

Named initializers require a known struct and existing, unique field names.
Each supplied value must match the field type using the existing scalar and
numeric-literal compatibility rules. Native numeric aliases still require
explicit casts when crossing into a different Sage type. Array initializers
validate element types and reject excess elements. Array fields preserve their
declared size in generated headers.

Omitted fields and array elements are zero-initialized by C. Empty `{}` is
accepted for a declared struct or fixed array and emits a zero initializer.
Zero initialization does not establish a native handle or string lifetime
contract. Positional struct initialization is unsupported.

| Code | Meaning |
|------|---------|
| S131 | Unknown struct field |
| S180 | Aggregate literal has no compatible destination shape or type context |
| S181 | Supplied field or array element has an incompatible type |
| S182 | Too many array initializer elements |
| S183 | Duplicate field declaration or initializer |

Existing `S144` checks still reject storing owning heap references in fields or
arrays. This increment does not add nested resource ownership, array copying or
array-return contracts, checked indexing, numeric range checks, or owned strings.
It also does not infer native ABI contracts from aggregate literals.

Validation: `tests/aggregate-contracts.ps1` includes positive and negative cases;
`Sandbox/tests/aggregate_contracts.test.sg` checks native execution, nested copies,
zero initialization, and typed arguments/returns.
