# Architectural audit (2026-10-24)

This is an implementation inventory, not a claim that the proposed ownership model
already exists. The baseline was `dotnet build` in Debug and Release, 13 Sandbox test
programs with 63 `[PASS]` markers and no `[FAIL]` markers in each configuration,
and the Sandbox savings calculator in Release.

## Current pipeline

`CompilationPipeline` discovers `use` dependencies, lexes and parses each source,
registers module symbols, analyzes the AST, emits one C source and header per Sage
source, then passes all generated C sources to a single GCC link command. It does
not separately compile reusable object files or maintain an incremental cache.
`Program` handles `--version`, `-v`, and `new`; the pipeline handles `run`, `build`,
and an explicit source path. Compiler errors now return a nonzero process status.

## Baseline memory limitations (before v0.8.2)

The subsequent [local ownership increment](memory.md) adds transfer and deletion
tracking. The findings below describe the baseline that motivated it.

- `SemanticAnalyzer` gates raw pointer declarations, casts, and indexing behind
  `unsafe` contexts, but it does not track ownership, borrows, moves, or lifetimes.
- `new` marks a struct expression as a reference and emits `malloc`; `delete`
  emits `free`. The analyzer checks the reference flag, but does not prove unique
  ownership, one deletion, or deletion on every exit path.
- Assigning a reference to another variable copies its C pointer. The analyzer
  does not invalidate either variable, so aliases can be freed twice or used
  after deletion. Ordinary scope exit does not emit cleanup.
- `str` maps to `char*`. String literals, interpolated strings, and native buffers
  currently share this surface type without a lifetime or ownership contract.
- `Sandbox/std/memory.sg` exposes raw allocation and release through `unsafe`
  functions. `file.sg` returns and closes raw `FILE*` handles; the compiler does
  not check their lifetime. `server.sg` uses `new` for an address passed to C and
  has no reachable cleanup in its unbounded loop.

These findings mean the current README's phrase "memory-safe by default" is
too broad. The existing protection is a syntactic boundary for some pointer
operations, not a general memory-safety proof.

## Other confirmed constraints

- `extern` declarations specify C header, native symbol, and optional link
  library. They do not encode pointer length, nullability, ownership transfer,
  or callback lifetime. No C++ declaration parser or adapter generator exists.
- `RegisterFunction` inserts unqualified and qualified names into a shared
  symbol table. `SymbolTable.Define` overwrites an existing entry; duplicate
  names from modules can therefore silently replace metadata. Module discovery
  keys files by basename and skips a later file with the same basename.
- `Sandbox/std/http.sg` launches `curl` through interpolated shell commands in
  its high-level helpers. Treat URL and payload inputs as untrusted until those
  wrappers stop constructing shell commands.
- Diagnostics carry codes and source coordinates, but there is no structured
  diagnostic output or compiler-derived semantic index. The VS Code extension
  currently parses test output markers rather than structured results.
- The parser and AST already retain source locations and `use` declarations.
  Those are usable foundations for later diagnostics and semantic indexing.

## Dependency order

1. Specify value categories, ownership transfer, borrowing contracts, and
   cleanup behavior with positive and negative compiler cases.
2. Add type capability metadata and flow-sensitive value state. Validate moves
   and borrows before emitting C.
3. Generate exactly-once cleanup for owned resources on all exits, then migrate
   `new`/`delete` examples and reject them in safe source.
4. Give C bindings explicit unsafe contracts and expose safe standard-library
   wrappers. Add bounded C++ adapter generation only after these contracts exist.
5. Strengthen module resolution, add extension methods, semantic indexing,
   structured diagnostics, then incremental compilation with cache validation.
   The semantic navigation tool is named **Graphify**.

Each implementation step needs executable positive and negative cases and the
Sandbox regression suite. The VS Code extension should consume stable CLI or
diagnostic contracts only after they are implemented in the compiler.
