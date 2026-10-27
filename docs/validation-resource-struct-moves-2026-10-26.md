# Validation: resource struct moves (v0.32.0, 2026-10-26)

Scope: whole-value moves of same-file structs that own native resources, owning
struct parameters and returns, named-owner moves into nested fields and array
elements, and replacement of elements or fields reached through local arrays.

Evidence:

- `tests/native-resource-struct-move.ps1`: 26 native runtime assertions with the
  instrumented tracker (`tests/fixtures/resource-struct-move.sg`) covering local
  moves, callee cleanup of adopted parameters, early return in the callee,
  conditional moves, factories returning named owners and literals, forwarding,
  whole and literal assignment, nested struct replacement, element and element
  field replacement, and resource-array element replacement. The tracker aborts
  on invalid or duplicate release and checks exact release order and counts.
- 29 compiler cases: use after move through every transfer form (S140), branch
  join (S142), loop (S145), self/expression/call-result replacement (S143),
  discarded and projected results (S193), non-owner arguments (S194), entry
  point return (S191), native ABI and borrowed struct signatures, interpolation,
  whole-array copies (S196), and constant destinations (S103).
- Import cases: a module may declare owning struct parameters; an importer
  cannot use a foreign resource struct layout by value (S196).
- Updated expectations in `native-resource-struct.ps1`,
  `native-resource-nested-struct.ps1`, `native-resource-struct-array.ps1`, and
  `native-resource-array.ps1`: former S196/S143 boundaries now compile, with
  new negative cases for moved sources and anonymous results.
- Full regression (`tests/*.ps1` and Sandbox tests) passes in Debug and Release.

Not covered: resource-struct borrows, moving an element or field out of an
aggregate, whole-array transfers, and imported layouts.
