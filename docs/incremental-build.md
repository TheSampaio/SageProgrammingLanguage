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
`NativeTranslationUnit.ForC`; other native sources (for example C++ adapters
compiled with `g++ -std=c++17`) can be added as further units, and
`NativeLinkRequest.Linker` selects the link driver (`gcc` by default).

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
