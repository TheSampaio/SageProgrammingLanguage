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
