# Safe resource presence queries — 2026-10-25

Compiler 0.17.0 supports contextual `null` equality/inequality checks on named
native resource owners and borrowed bindings. They return a Copy boolean without
exposing a native pointer or transferring ownership. Existing bindings named
`null` keep their ordinary meaning. This does not introduce general null values,
resource construction, non-null type refinement, or safe native I/O operations.

Completed in Debug and Release:

- Builds passed with zero warnings/errors; all 18 PowerShell regression scripts
  passed. Sandbox: 22 files and 146 assertions passed in each configuration.
- Presence suite: 24 cases, initially red with S105 on compiler 0.16.0. Includes
  both operand orders, equality/inequality, shared/mutable/owning parameters,
  lexical loans, contextual shadowing, moves, anonymous producers, and invalid
  comparisons. A minimal native header proves the NULL include dependency;
  verbose AST inspection verifies the validated comparison marker.
- Native resource instrumentation: 66 runtime assertions and 31 ownership cases
  plus the existing minimal-header check. New assertions cover live/empty handles,
  safe borrowed queries, unchanged ownership, and exactly-once final destruction.
- Existing parameter (32), return (23), borrow (33), and declaration (31) suites
  remained green, alongside the other compiler regressions.

The semantic marker controls private-handle C lowering. S195 rejects unsupported
null comparisons. Moved owners still fail S140; active mutable loans still block
owner access with S178. Queries nested in other arguments of consuming calls must
be evaluated into a local boolean first (S194), even for separate resources, so
C argument moves cannot change an inspected handle before its query. Borrow-only
calls can receive pure query expressions directly.

Resource aggregates/globals/general owning temporaries and safe file/string/buffer
APIs remain future work. Native validity/exclusivity, destructor consumption,
unsafe raw aliases, and process termination behavior retain their existing limits.
