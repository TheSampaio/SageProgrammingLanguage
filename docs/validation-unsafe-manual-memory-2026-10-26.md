# Validation: `new` / `delete` require unsafe (v0.32.2, 2026-10-26)

Scope: remove manual memory management from the safe surface. `new` and
`delete` outside an unsafe context are rejected with S133 before C generation;
inside `unsafe` the legacy reference checks are unchanged.

Evidence:

- `tests/memory-manual-allocation.ps1`: 12 cases. Safe `new` in initializers,
  projections, helpers, and globals, and safe `delete` (including of a borrow
  and after an unrelated unsafe block) report S133; `unsafe` blocks and
  `unsafe func` bodies compile and run; double deletion inside unsafe is still
  S141; plain-value replacements compile. The diagnostic format and message are
  asserted.
- Migrated tests keep their original assertions by moving legacy reference
  cases into `unsafe func`: `memory-ownership.ps1`, `allocation-contexts.ps1`,
  `aggregate-contracts.ps1`, `memory-borrowing.ps1`,
  `memory-borrow-lifetimes.ps1`, `native-resource-borrowing.ps1`, and the
  `ownership-cleanup.sg` instrumented fixture. A global allocation now reports
  S133 (globals cannot be unsafe) instead of S146.
- Sandbox: `src/main.sg` uses a plain struct value with identical output;
  `borrowing`, `borrow_lifetimes` use stack values and add an unsafe heap-borrow
  check; `new_delete`, `ownership`, and `aggregate_contracts` exercise `new` and
  `delete` inside `unsafe` and add safe value equivalents.
- `std/http_server.sg` already allocates inside `unsafe func serve`.
- Full regression passes in Debug and Release.
