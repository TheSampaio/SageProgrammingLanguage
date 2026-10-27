# Incremental native builds

Sage compiles each project in two halves: the front end (lexing, parsing,
semantic and ownership analysis, C generation) and the native build (GCC). This
document describes how the native half is organized and what it reuses.

## Module dependency graph

`Core/Build/ModuleGraph` records one node per Sage module and one edge per
`use` declaration. Modules keep discovery order (the entry file first, then
breadth-first through `use`), edges keep declaration order, and repeated `use`
declarations collapse into one edge. Cycles are tolerated exactly as module
discovery tolerates them: each module is parsed once.

`--verbose` prints the graph in a compact, deterministic form:

```text
Module graph: main -> alpha, beta; alpha -> util, beta; beta -> alpha, util; util -> (none)
```

Each generated module header includes the headers of the modules it uses, so
the graph is also the shape of the generated C include graph.

## Separate compilation

`Core/Build/NativeBuilder` compiles every native translation unit on its own
and links the resulting objects:

```text
gcc -c obj/<module>.c -o obj/<module>.o -std=c11 -I<obj> -I<compiler>/Runtime/include
gcc obj/main.o obj/alpha.o ... -o bin/<app>.exe -l<library> ...
```

The flags, include directories, link libraries (collected from `extern` blocks
and passed in ordinal order), and object order (module discovery order) match
the previous single GCC command. Units compile in parallel, bounded by the
processor count. A failing unit makes the build fail with GCC's diagnostic; when
several fail, their messages are reported in unit order, and a missing compiler
is reported as before. Exit-status propagation (`run`, explicit file) is
unchanged.

`NativeTranslationUnit` is language-neutral: source path, object path, compiler
driver, flags and include directories. Generated C modules use
`NativeTranslationUnit.ForC`. Since v0.37.3 declared project sources and the
generated C++ adapters are units too: `.c` sources compile with `gcc -std=c11`
into `obj/sage_native_c_<n>_<name>.o`, C++ sources and `sage_cpp_adapters.cpp`
with `g++ -std=c++17` (`NativeTranslationUnit.ForCpp`). Every unit shares the
generated, runtime, and declared-source include directories; adapters also see
the project source directory. `NativeLinkRequest.Linker` is `g++` when any C++
unit exists and `gcc` otherwise, so C-only projects never invoke `g++`.

## Incremental cache

Since v0.36.2 the native build reuses objects and executables whose inputs are
unchanged. The front end (parse, semantic and ownership analysis, C generation)
still runs for every module on every build; only native compilation and linking
are cached.

### Object cache key

An object is reused only when all of the following are unchanged, and the
object on disk still has the content hash recorded when it was produced:

| Input | How it is captured |
| --- | --- |
| Generated C source | SHA-256 of the file content |
| Every header it includes, transitively | SHA-256 of each generated module header, the `sage.h` prelude and `Runtime/include` headers |
| System headers (`<stdio.h>`, ...) | Recorded by name; their content is covered by the toolchain identity |
| Sage compiler version | `SageInfo.Version` |
| Native compiler and flags | Driver command (`gcc`), flags (`-std=c11`), include directory count |
| Target and toolchain identity | SHA-256 of `gcc -v` (version, target triple, configuration, install paths), probed once per build |
| Source location | Absolute path of the generated C file |

Headers are found the way GCC finds them: a quoted include searches the
including file's directory first, then the `-I` directories in order; an angle
include searches only the `-I` directories. Every `#include` line counts,
whether or not it is inside a conditional block, which can only cause extra
rebuilds. Header locations are recorded relative to their include directory, so
identical headers at different paths (for example the Debug and Release copies
of `Runtime/include`) produce the same key: the Debug and Release compilers
share one valid cache.

Consequences:

- A function-body change leaves the module header unchanged, so only that
  module recompiles.
- A public interface change rewrites the module header, so the module and every
  module whose header closure includes it (its transitive dependents in the
  module graph) recompile.
- A changed runtime header recompiles exactly the units that include it.
- A new compiler version, toolchain, flag set or source location recompiles
  everything it affects.

### Links

The executable is relinked when its link key changes (linker driver and
identity, the full link command including libraries and object order, and the
content hash of every object) or when the executable is missing or no longer
has its recorded hash. GCC produces byte-identical objects for identical
inputs, so recompiling a unit without a real change does not force a relink.

### Manifest, locking and recovery

The cache is recorded in `obj/sage-build-cache.json`, written to a temporary
file and then moved into place. A missing, unreadable or incompatible manifest
is discarded (`--verbose` reports it) and every unit is rebuilt. Stale entries
are removed before compiling, so a failed or interrupted compile is never
mistaken for a valid object; successfully compiled units stay cached even when
another unit fails. Builds sharing an `obj/` directory are serialized through
`obj/.sage-build.lock`.

### Forcing a clean build

Add `--clean` to `sage build`, `sage run` or an explicit file to delete the
manifest and every object in `obj/` before building. Deleting `obj/` has the
same effect. Existing commands and their exit statuses are unchanged.

`--verbose` prints a one-line summary:

```text
Native compile: 1 compiled [util], 4 reused [main, alpha, beta, probe]; link: relinked
```

### Limitations

- Computed includes (`#include MACRO`) are not resolved; Sage never generates
  them and the runtime headers do not use them.
- `__FILE__`-style path expansion is not part of the key. Generated code and
  runtime headers do not use it.
- The front end is not incremental: semantic analysis is whole-program.
- A changed toolchain is detected through `gcc -v`; replacing system headers in
  place without changing the toolchain installation is not detected (delete
  `obj/` or use `--clean`).

## Development instrumentation

Phase timings, cache counts and memory statistics are compiled only into a
dedicated development configuration. Every recording call in
`Utilities/CompilerInstrumentation` is `[Conditional("SAGE_INSTRUMENTATION")]`,
so Debug and Release compilers contain no instrumentation calls, stay quiet,
and write no report files.

```powershell
dotnet build Sage.csproj -c Instrumented        # bin/Instrumented/net8.0/Sage.dll
# or: dotnet build Sage.csproj -c Release -p:SageInstrumentation=true
```

An instrumented compiler prints one `name=value` line per metric to standard
error after every run:

| Metric | Meaning |
| --- | --- |
| `lex_ms`, `parse_ms` | Lexing and parsing, summed over modules |
| `semantic_ms` | Symbol registration, native contract validation and analysis; ownership and borrow checking run inside the analyzer and are not separable |
| `codegen_ms` | Header and C generation, including writing the files |
| `cache_ms` | Include-closure hashing, cache keys and object validation |
| `native_compile_ms`, `link_ms` | Wall time of the parallel `gcc -c` step and of the link check/link |
| `toolchain_probe_ms` | `gcc -v` probe; it starts with the front end and overlaps it |
| `run_ms` | Running the application (`run` and explicit-file builds) |
| `units_compiled`, `units_reused`, `relinked` | Cache outcome |
| `process_ms` | Wall time since process start, including .NET startup |
| `peak_working_set_bytes`, `allocated_bytes`, `gc_collections` | Peak memory, managed allocation and GC counts (gen0/gen1/gen2) |

## Benchmarks

`tools/bench-incremental.ps1 -CompilerDll <Sage.dll> [-Modules 40]
[-FunctionsPerModule 20] [-Iterations 5] [-OutputJson <file>]` generates a
synthetic project in a temporary directory (module `i` uses modules `i-1` and
`i/2`; `main` uses all of them), performs one unrecorded warm-up build, then
times `sage build` (wall clock, including .NET startup) in four scenarios per
iteration: **clean** (`obj/` and `bin/` deleted, so older compilers work too),
**no-change**, **body** (one function body edited in module `m<N/4>`, which has
many dependents) and **interface** (a public function added to that module).
It records the compiler version and build configuration, GCC version and
target, .NET SDK, OS, CPU, core count, RAM, workload and iteration count. With
an Instrumented compiler it also reports how many units compiled.

### Measured results (2026-10-26)

Environment: Intel Core i5-10400F (6 cores / 12 threads), 15.9 GB RAM, Windows
NT 10.0.26200, GCC 15.2.0 x86_64-w64-mingw32 (w64devkit), .NET SDK 10.0.401,
Release compilers, 5 iterations, median (min-max) milliseconds. The baseline is
v0.30.0 (commit `9b080dd`, one GCC command for all sources) built from a
`git archive` export; v0.36.4 is this branch. Both ran in the same session.

41 modules (40 generated + main), 20 functions each, hot module `m010`:

| Scenario | v0.30.0 | v0.36.4 | Units compiled (Instrumented) |
| --- | --- | --- | --- |
| clean | 4098 (4052-5004) | 2015 (1944-2091) | 41 |
| no-change | 4141 (4043-4298) | 665 (654-719) | 0 |
| body | 4134 (4077-4274) | 963 (923-984) | 1 |
| interface | 4128 (4025-4290) | 1731 (1727-1832) | 31 |

5 modules (4 generated + main), 5 functions each, hot module `m001`:

| Scenario | v0.30.0 | v0.36.4 | Units compiled (Instrumented) |
| --- | --- | --- | --- |
| clean | 519 (516-537) | 454 (435-474) | 5 |
| no-change | 521 (514-566) | 188 (179-194) | 0 |
| body | 524 (508-544) | 420 (407-424) | 1 |
| interface | 535 (512-537) | 442 (434-463) | 4 |

Observations (from the measurements above and one instrumented run of the
41-module workload, not averaged):

- Clean builds are faster because units compile in parallel; the baseline ran
  all sources through one sequential GCC command.
- A no-change rebuild of the 41-module project spends roughly 490 ms in
  semantic analysis, 90 ms in C generation and 50 ms in cache validation; GCC
  is not invoked. The front end is now the dominant cost.
- A one-unit rebuild costs about one `gcc -c` (~100-160 ms) plus the link
  (~200 ms).

These are measurements on one machine; other hardware, antivirus scanning and
disk caches change absolute numbers.

## Precompiled headers: investigated, not implemented

Measured on the same machine (20 compiles each, `gcc -c -std=c11`): an empty C
file takes about 72 ms per compile (process start-up and code generation), the
same file with the system headers Sage modules use (`stdint.h`, `stdbool.h`,
`stdio.h`, `stdlib.h`, `string.h`, `stddef.h`, `limits.h`) about 96-97 ms, a
generated `console.c` about 104 ms, and the header file through a precompiled
`common.h.gch` (4.07 MB) about 81 ms. A PCH would therefore save about 16 ms per
compiled unit.

That is about 1.5% of a one-unit rebuild of the 41-module project and, spread
over 6 parallel workers, roughly 110 ms of a 2 s clean build, before paying for
generating the `.gch` (one extra compile per flag set) and keeping it in the
cache key. GCC also requires the precompiled header to be the first include of
every translation unit with identical flags, which would force a common
generated header ahead of each module header. The estimated gain does not
justify that coupling, so Sage does not use PCH. Revisit if generated modules
start including substantially larger headers.
