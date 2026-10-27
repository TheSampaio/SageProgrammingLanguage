# Phase 8 final validation - 2026-10-26

Integrated `develop` at Sage 0.38.1 after merging Phases 2-7 (Phase 3
`feat/cpp-interop`, Phase 2 `feat/memory-completion`, Phase 5
`feat/extension-methods`, Phase 6 `feat/graphify`, Phase 7
`feat/incremental-build`, Phase 4 `feat/std-migration`).

## Regression

| Configuration | PowerShell scripts | Sandbox files | `[PASS]` | Failures |
| --- | --- | --- | --- | --- |
| Debug | 51 | 30 | 291 | 0 |
| Release | 51 | 30 | 291 | 0 |

Baseline before this round (0.30.0): 29 scripts, 26 files, 203 `[PASS]`.
The Sandbox savings calculator produced identical output and exit status for a
`--clean` build and the following incremental build. `-v`, `--version`, `new`
and `run` were exercised; `tests/cli-new-project.ps1` covers the new project path.
Sandbox apps and examples contain no `unsafe`, `new`, or `delete` code.

## Merge integration decisions

- Generated C symbol collisions are detected once, by `NativeSymbolRegistry`
  (`S212`); the parallel Phase 5 table (`S155`) was removed. A module function
  named `main` now collides with the entry point.
- Extension methods reject by-value receivers of structs that own native
  resources (`S157`), because Phase 2 made those structs movable.
- Declared C/C++ sources and generated C++ adapters are cached translation units
  of the incremental builder; `g++` links only when a C++ unit exists.
- Every diagnostic code added by Phases 2, 3 and 5 is in `DiagnosticCatalog`
  and [diagnostics](diagnostics.md); the catalog guard test enforces this.

## Benchmark (measured)

`tools/bench-incremental.ps1`, Release, Sage 0.38.1, GCC 15.2.0
x86_64-w64-mingw32, .NET 10.0.401, Windows NT 10.0.26200, i5-10400F
(6 cores / 12 threads), 15.9 GB RAM, 41 modules x 20 functions, 5 iterations:

| Scenario | Median ms | Min ms | Max ms |
| --- | --- | --- | --- |
| clean | 2348 | 2208 | 3188 |
| no-change | 672 | 662 | 714 |
| body edit | 1052 | 1025 | 1955 |
| interface edit | 1969 | 1924 | 2574 |

See [incremental builds](incremental-build.md) for the 0.30.0 comparison.

## Sanitizers

Not available on this toolchain: GCC 15.2.0 (w64devkit) ships no `libasan` or
`libubsan`, so `-fsanitize=address` and `-fsanitize=undefined` fail to link.
Only `-fsanitize-trap=undefined` links. Memory behavior of generated code is
validated instead by native allocation instrumentation in the resource,
cleanup, text, vector and HTTP tests (exactly-once release and order).

## Completion criteria (original brief, section 21)

| # | Criterion | Status |
| --- | --- | --- |
| 1 | Safe ownership-based memory management | Implemented for the documented subset; see [memory](memory.md) "Guarantees and limits". |
| 2 | Deterministic resource cleanup | Implemented (scope exit, early return, loops, moves, nested structs, arrays). Process termination does not unwind. |
| 3 | No manual `new`/`delete` in public code | Implemented as rejection outside `unsafe` (`S133`); both remain inside the trusted boundary. |
| 4 | No raw pointers in the safe surface | Implemented: pointer declarations, casts and indexing require `unsafe`. |
| 5 | Static detection of invalid ownership/borrowing | Implemented with stable codes. Limits: no borrows of resource structs, no field-stored or returned borrows, conservative loop rules. |
| 6 | Safe interfaces over C interop | Implemented: explicit unsafe native calls, resource contracts, FFI safety review in [native boundaries](native-boundaries.md). |
| 7 | Automatic C++ wrapper generation | Implemented for explicitly declared constructs (functions, overloads, templates, constructors, destructors, members, static members, handles, recoverable exceptions). No automatic header parsing: no Clang toolchain is available. |
| 8 | Ownership-aware std | Implemented: `text`, `int_vector`, `byte_file`, `byte_buffer`, shell-free HTTP, owned listener. Limits: `i32`-only vector (no generics), HTTP Windows-only, legacy `file`/`memory` kept unsafe and deprecated. |
| 9 | Module-based extension methods | Implemented (`method func`, static resolution, ambiguity `S158`). |
| 10 | Deterministic module/symbol resolution | Implemented (direct-import visibility `S153`, C symbol collisions `S212`). Struct/resource type names remain global across files. |
| 11 | Compiler-derived semantic index | Implemented (Graphify). Limits: locals not indexed; full re-analysis, incremental shard writes. |
| 12 | Correct incremental compilation | Implemented for the native half (content-addressed objects, tested against clean builds). The front end still runs fully. |
| 13 | Human and machine-readable diagnostics | Implemented (`--diagnostics json`, stable catalog). |
| 14 | Reproducible tests and benchmarks | Implemented; sanitizers unavailable (above). |
| 15 | Clean history of validated atomic commits | Phase branches merged with `--no-ff`. One Phase 5 commit added its doc a commit early; history was not rewritten. |

The companion VS Code extension 0.19.0 (`codex/sage-extension`) highlights the
new syntax, maps JSON diagnostics to the Problems panel, adds build and Graphify
commands, and still contributes no icon theme.
