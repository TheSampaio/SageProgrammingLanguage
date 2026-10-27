# Local native resource ownership — 2026-10-25

Compiler 0.13.0 adds unsafe local adoption, named-owner moves, trusted native
handle access, and typed GCC cleanup for declared resources. Borrowing, value
parameters/returns, globals, resource arrays/fields, and safe file APIs remain
disabled. See [the contract and its preconditions](native-resource-contracts.md).

Validation completed in both Debug and Release:

- .NET builds: zero warnings and errors.
- All 14 PowerShell regression scripts passed, including existing allocation,
  ownership, borrow, aggregate, array, callable, module, native boundary, and CLI
  checks.
- Native resource declarations: 31 contract cases.
- Native resource ownership: 25 native runtime assertions, 31 positive/negative
  cases, and a minimal-header check for the generated NULL dependency. The new
  fixture first failed with S191 on compiler 0.12.0 before implementation.
- Sandbox: 22 files, 138 passing assertions, no failures in each configuration.
- The permanent native resource declaration example compiled and ran in both.

The native fixture tracks live handles and rejects invalid/duplicate destruction.
It verifies reverse cleanup order, early return evaluation, null/moved handles,
const moves, shadowed moves/adoption, loop cleanup, void destructors, and an
integer destructor failure status. The latter consumes the handle and returns
an error: implicit cleanup calls it once and continues, without retry/propagation.
Semantic cases reject unsupported value operations and anonymous/invalid
adoption before C generation. Imported local ownership is also covered.

These checks establish the bounded implemented behavior, not arbitrary native
pointer validity or all memory safety. The declared ABI, exclusive acquisition,
destructor consumption, and unsafe raw alias lifetime are trusted preconditions.
