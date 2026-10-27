# Sage 0.33.1 owned int vector validation

Validated on Windows, 2026-10-26:

- Debug and Release builds: 41 PowerShell scripts passed; Sandbox passed
  29 files / 280 assertions in each configuration.
- `tests/int-vector.ps1`: native contract compiled with `-Wall -Wextra -Werror`
  and an allocator that fails on demand: null owners, owner allocation failure,
  limit overflow and > `u32` limits rejected before allocation, deferred element
  storage, growth 4 -> 8 -> clamped to the limit, failed growth preserving
  length/capacity/elements, the exact-slot retry, `full` at the limit, reserve
  bounds, checked get/set/pop, clear keeping storage, and drop freeing both blocks.
- 26 Sage assertions from `Sandbox/tests/int_vector.test.sg`.
- The shared test-only allocation tracker (logging malloc/realloc/free) records
  22 allocations and 22 frees across owning returns, moves, early return,
  consuming parameters, loop scopes and reassignment; no unknown or double free.
- 11 rejected programs fail before C generation (S140, S171, S177, S159, S178,
  S194, S164, S109, S161).

No compiler code changed; the increment adds `Sandbox/std/int_vector.sg` and
`Runtime/include/sage_int_vector.h`.
