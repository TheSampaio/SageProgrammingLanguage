# Owning native resource returns — 2026-10-25

Compiler 0.15.0 transfers named local resource owners through Sage returns.
The callee clears the returned source before cleanup; the call result must
directly initialize a new local owner. General owning temporaries and consuming
parameters remain disabled in this increment. Native ABI resource returns and
resource return types on the entry point are rejected.

Debug and Release validation completed:

- Builds: zero warnings/errors; all 16 PowerShell regression scripts passed.
- Resource returns: 23 positive/negative cases, initially red with S191 on
  compiler 0.14.0. Coverage includes const, branches, forwarding through locals,
  forward declarations, modules/imports, unsafe factories, live loans, and
  rejection of discarded/projected/anonymous results or invalid returns.
- Resource native instrumentation: 44 runtime assertions and 31 ownership cases
  plus the minimal-header check. Returned handles remain live and retain identity;
  other owners are destroyed, and the final caller destroys once. Null returns,
  early/nested returns, and multiple forwarding functions are covered.
- Existing resource borrow (33) and declaration contract (31) cases passed.
- Sandbox: 22 files, 142 assertions passed in each configuration.

The return lowering uses a typed GNU statement expression to preserve the value
before clearing its source. This extends the existing GCC backend contract.
Native validity, exclusive acquisition, destructor consumption, and unsafe raw
alias lifetimes remain trusted. No safe file API or resource aggregates are
claimed by this increment.
