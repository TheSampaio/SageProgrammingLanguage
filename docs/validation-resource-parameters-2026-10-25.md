# Consuming native resource parameters — 2026-10-25

Compiler 0.16.0 supports Sage resource value parameters as ownership transfers.
Arguments must name local owners or owning parameters. The caller is cleared;
the callee adopts cleanup before the body. Moving, returning, or forwarding
the parameter clears its source. Borrowed values cannot be consumed.

Validation completed in Debug and Release:

- Builds: zero warnings/errors; all 17 PowerShell regression scripts passed.
- Consuming parameter tests: 32 cases and a minimal-header check, initially red
  with S191 on compiler 0.15.0. Coverage includes const, multiple parameters,
  identity returns, forwarding, borrowing, imports/modules, forward declarations,
  branch availability, and invalid use after transfer, call conflicts,
  effectful arguments, entry-point parameters, or native ABI declarations.
- Native resource instrumentation: 58 runtime assertions, 31 ownership cases,
  and the original minimal-header check. It proves cleanup for unused parameters,
  early returns, identity returns, forwarding, and mixed ownership/borrowing.
  Body locals drop first, then parameters in reverse declaration order. Native
  reads now validate handle liveness before dereferencing, so a regression reports
  invalid access instead of dereferencing a released test handle.
- Resource return (23), borrowing (33), and declaration (31) cases stayed green.
- Sandbox: 22 files and 144 assertions passed in each configuration.

The compiler rejects consuming and borrowing the same resource in either
argument order. Other arguments must use the supported pure argument subset,
so checked moves do not depend on C argument evaluation order. Resource owner
and borrow parameters are rejected on entry points. Native ABI resources,
resource aggregates/globals, and general owning temporaries remain disabled.
Unsafe raw aliases, native validity, exclusive acquisition, and destructor
consumption retain their documented trusted obligations.
