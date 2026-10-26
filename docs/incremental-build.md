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
