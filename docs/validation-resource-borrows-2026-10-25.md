# Native resource borrows — 2026-10-25

Compiler 0.14.0 supports explicit shared/mutable resource parameters and lexical
borrowed locals without transferring ownership. Resource borrows reuse existing
loan identities, call conflicts, reborrow suspension, const restrictions, and
scope checks. Moving a borrowed resource into an owning local is rejected.

Validation completed in Debug and Release:

- Builds succeeded with zero warnings/errors; all 15 PowerShell regression
  scripts passed.
- New borrowing regressions: 33 cases, initially red with S191 on compiler
  0.13.0. Cases include shared/mutable parameters and locals, reborrowing, const,
  module/import use, source-order-independent prototypes, and invalid ownership
  transfer, alias conflicts, effectful arguments, escapes, and compositions.
- Native resource instrumentation: 32 runtime assertions and 31 ownership cases,
  plus the minimal-header check. The fixture verifies that borrowed calls do not
  adopt/release handles, mutation reaches the original handle, owner movement
  resumes after loans end, and the final owner destroys exactly once.
- Declaration contracts: 31 cases.
- Sandbox: 22 files and 141 passing assertions in each configuration.

Generated facade typedefs are collected before function prototypes, including
functions textually preceding the resource declaration and declarations inside
modules. Existing scalar/struct borrowing, array, allocation, CLI, module, and
native boundary regressions all remained green.

By-value resource parameters/returns and resource globals/fields/arrays remain
disabled. Borrow returns and native ABI borrowing remain disabled. Raw native
aliases, shared/mutable discipline inside unsafe wrappers, native validity, and
destructor consumption remain trusted preconditions; this is not a claim of
complete native lifetime checking or a safe standard file API.
