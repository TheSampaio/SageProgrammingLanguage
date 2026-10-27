# CLAUDE.md — Working rules for the Sage project

Guidance for AI assistants (and humans) working in this repository. Read this before making changes.

## What this is

Sage is a statically typed programming language that **transpiles to C11** and
then invokes GCC to produce a native `.exe`. The compiler is C# (.NET 8). See `README.md` and
`ARCHITECTURE.md` for the full picture.

Companion project (separate repo): the VS Code extension at
`../SageExtensionCode/thesampaio-vscode-sage-programming-language/`.

## Build, run, test

```powershell
# Build
dotnet build Sage.csproj -c Debug
dotnet build Sage.csproj -c Release        # "Dist" build

# Run the sandbox app (Debug uses the Sandbox/ fallback when no args are given)
dotnet run --project Sage.csproj -c Debug

# Run the compiler on an explicit file (works in Release too)
dotnet bin/Release/net8.0/Sage.dll Sandbox/src/main.sg
```

**Always verify changes in BOTH Debug and Release.** Release disables the debug-only sandbox
fallback, so it exercises the real CLI/argument path.

### Tests

Unit tests are Sage programs under `Sandbox/tests/*.test.sg` that use the `test` std module and
print `[PASS]`/`[FAIL]` lines. Run one with `Sage.dll <file>` and count the markers. After any
compiler change, run the whole suite in Debug **and** Release and confirm 0 failures.

## Golden rules

1. **Bump the compiler version on every compiler change.** The single source of truth is
   `Utilities/SageInfo.cs` (`Version`). Keep the version line in `README.md` (Project Status) in sync.
2. **Commit in parts, in English**, each part building + passing tests. End commit messages with the
   `Co-Authored-By: Claude ...` trailer. Leave opening the PR and merging to the user (help with the
   text only).
3. **Follow Clean Code / SOLID / DRY / KISS.** Match the existing style (XML doc comments, primary
   constructors, the Visitor pattern).

## Architecture cheat-sheet

Pipeline: Lexer → Parser (recursive descent → AST) → SemanticAnalyzer → CodeGenerator (+ HeaderGenerator)
→ GCC. Orchestrated by `Core/CompilationPipeline.cs`.

- `Ast/` — immutable AST nodes; each implements `Accept` (Visitor pattern).
- `Interfaces/IAstVisitor<T>` — the visitor contract.
- `Core/` — `Lexer`, `Parser`, `SemanticAnalyzer`, `CodeGenerator`, `HeaderGenerator`, `TypeSystem`,
  `CNaming`, `SymbolTable`.
- `Enums/TokenType.cs`, `Utilities/` (logger, config, environment, AST/token printers, `SageInfo`).
- Callables are visible only in their declaring file and files that directly `use` it (S153);
  native aliases are file-private; distinct callables lowering to one C symbol are S212.
  See `docs/modules.md`.
- `method func` (contextual `method`, modules only) opts a function into `value.f(args)`;
  `MethodCallNode.Lowered` holds the resolved `Module::f(receiver, args)` call that analysis and
  codegen reuse (S156-S159). See `docs/extension-methods.md`.

### Adding a language feature (the standard workflow)

1. Token: add to `Enums/TokenType.cs` and map it in `Core/Lexer.cs` (or handle contextually).
2. AST: add an immutable node in `Ast/` with `Accept`.
3. **Visitor contract**: add `Visit(NewNode)` to `IAstVisitor<T>`. This forces you to update **all four**
   visitors or the build breaks — a feature, not a chore:
   `SemanticAnalyzer`, `CodeGenerator`, `HeaderGenerator`, and `Utilities/AstPrinter`.
4. Parser: consume the tokens, produce the node.
5. Semantics + codegen: rules in `SemanticAnalyzer`, C output in `CodeGenerator`/`HeaderGenerator`.

## Memory-safety model

- Raw pointers (`T*`, incl. `none*`) are only allowed inside an `unsafe { }` block or an `unsafe func`.
  `extern` is implicitly unsafe. `str` is a safe string type, not a raw pointer. Gating lives in
  `SemanticAnalyzer` via an unsafe-context depth counter.
- `new`/`delete` are unsafe-only since v0.32.2 (S133); safe code uses plain values,
  borrows, and resource owners (migration in `docs/memory.md`).
  `new T { ... }` allocates on the heap and yields a **reference** (surface type `T`,
  represented as `T*` in C with `->` access; no pointer arithmetic). Freed with `delete` (→ `free`).
  Reference-ness is carried by `AstNode.IsReference` and `SymbolMetadata.IsReference`.
  Local reference initialization now transfers ownership. Use after transfer or deletion
  is rejected; see `docs/memory.md`. Local owners have automatic GCC scope cleanup;
  `&T` and `&mut T` support call-duration parameters and lexical local borrows for scalar/struct values;
  see `docs/borrowing.md` for the exact supported subset and restrictions.
- `const` is enforced (`SymbolMetadata.IsConstant`).

## FFI / stdlib conventions

- **Type mapping is centralized in `Core/TypeSystem.ToCType`** — the single source of truth. Don't
  scatter C-type logic elsewhere. `CNaming.ResolveFunctionName` and `TypeSystem.FormatCParameter` are
  shared by the code and header generators; keep them the one place those rules live.
- Write `extern` bindings with **C interop aliases** (`c_int`, `c_uint`, `c_char`, `c_size_t`,
  `c_void`, ...) so they match real C signatures. Public wrappers may expose ergonomic Sage types.
- Crossing the FFI boundary into a Sage type needs an explicit cast (`... as i32`) — c_* aliases are
  numeric scalars but are not implicitly convertible in assignments.
- Opaque C types: `type FILE;` inside an extern block (emitted verbatim to C; used via pointers).
- Link libraries: `extern winsock("winsock2.h", "ws2_32") { ... }`; the pipeline forwards these as
  `-l` flags. Missing an address-of operator, use a `new` struct reference to hand C a pointer
  (see `Sandbox/std/http_server.sg`).
- Wrappers that traffic in raw pointers must be `unsafe func` (see `Sandbox/std/memory.sg`).
- Every direct `extern` call requires `unsafe`; regular wrappers isolate trusted
  native operations locally. Legacy `console.read_line` and shell-based HTTP
  helpers require unsafe callers; see `docs/native-boundaries.md`.
- Native resources use explicit `resource Name: NativePointer drop destructor;`
  declarations. Unsafe direct local adoption and named-owner moves have automatic
  nullable-handle cleanup; see `docs/native-resource-contracts.md`. Explicit shared/
  mutable borrows reuse lexical loan checks. Named resource owners may be returned
  into direct local owning call initializers. Resource value parameters consume
  named owners and adopt cleanup in the callee. Local resource arrays and
  direct or nested struct fields have bounded contracts; see S193/S194 producer
  and call restrictions in the contract.
- `owner == null` / `owner != null` checks a named resource or borrow in safe
  Sage. `null` is contextual and existing bindings keep their meaning. Bind a
  presence query before passing it alongside consumed arguments; see S195/S194.
- Standalone `destination = source;` between available named native resource
  owners moves the source and drops the old destination. Self-assignment and
  conflicting loans remain rejected; see `tests/native-resource-reassignment.ps1`.
- Structs with direct or nested native resource fields support local literal
  initialization from named owners and recursive reverse-field cleanup. Empty
  and omitted fields are zeroed. Same-file resource structs are non-Copy values
  that move whole (init, standalone assignment, owning parameters, returns,
  nested fields, array elements); the source is zeroed so cleanup runs once.
  Replacement works through field and checked-element paths. Borrows, imported
  layouts, and whole-array transfers remain S196; see `docs/resource-structs.md`
  and `tests/native-resource-struct-move.ps1`.
- Fixed local arrays of native resources or same-file resource structs accept
  direct literals or zero initialization. Cleanup drops elements in reverse
  index order and recursively drops struct fields. Array copies, field storage,
  and element extraction remain rejected; see `docs/resource-arrays.md` and
  `tests/native-resource-struct-array.ps1`.
- `byte_file` provides bounded owned byte I/O and consuming close with explicit
  outcomes. `create_bytes(u8[260], u16)` safely accepts 1..259 printable ASCII
  bytes; `create(str)` remains unsafe. Retain the legacy `file` module. The std
  distribution contains only `.sg`. Native headers ship with the compiler in
  `Runtime/include`; see `docs/byte-files.md`.
- `byte_buffer` provides exclusive fixed-capacity storage with `u32` length/capacity,
  shared checked reads, mutable write/push/clear, and consuming release. Spare
  capacity and cleared bytes are unreadable until pushed again. Native allocation
  support stays in `Runtime/include`; see `docs/byte-buffers.md`.
- `byte_file.read_buffer` replaces the initialized prefix after native input;
  `write_buffer` reads only an initialized shared prefix. Both borrow the file
  exclusively and return `ByteTransfer { count, eof, failed }`, preserving partial
  counts on error. Validate limits before I/O; zero-limit permitted calls are no-ops.
  Native status is captured immediately within the wrapper's exclusive borrow.
- `byte_file.open_read_bytes` opens existing binary input without truncation.
  Native mode fixes output capability: `can_write` reports it; read-only handles
  reject byte/block writes and flush before stdio, including zero-limit writes.
  Do not add input-only modes without keeping incompatible output guarded.
- `extern cpp alias("header.hpp") { ... }` binds explicit C++ targets (`= ns::f`,
  `= "ns::f<int>"`, `= .member`, `= new`, `= delete`; `class H = ns::T;` handles)
  through generated `extern "C"` adapters (`CppAdapterGenerator`, validated by
  `CppInteropValidator`, S210). Exceptions abort; only C++ builds invoke `g++`.
  See `docs/cpp-interop.md` and `tests/cpp-interop.ps1`.
- `source "x.c|x.cpp";` inside any extern block compiles a project native source
  (path relative to the declaring .sg; S211 if missing/unsupported) via
  `NativeSourceSet`; see `tests/native-sources.ps1`.

## Incremental native builds

- `Core/Build/ModuleGraph` holds the `use` dependency graph (discovery order,
  cycles tolerated); `--verbose` prints it. See `docs/incremental-build.md`.
- `Core/Build/NativeBuilder` compiles each `NativeTranslationUnit` (source + compiler +
  flags + include dirs) with `-c` into `obj/<name>.o`, then links. New native sources
  (e.g. C++ adapters) plug in as extra units in `CompilationPipeline.LinkNativeBinary`.
- Objects/links are cached in `obj/sage-build-cache.json` (content-addressed: generated C +
  transitive headers + `SageInfo.Version` + compiler/flags + `gcc -v` identity). `--clean`
  forces a rebuild. Anything new that affects object code must enter the unit's key.
- Development metrics: `dotnet build -c Instrumented` defines `SAGE_INSTRUMENTATION`;
  `CompilerInstrumentation` calls are `[Conditional]`, so Debug/Release stay quiet.
- `tools/bench-incremental.ps1 -CompilerDll <dll>` benchmarks clean vs incremental builds;
  record measured results in `docs/incremental-build.md` (PCH was measured and rejected).

## Generated C

- A single shared prelude `sage.h` holds the type aliases; every module header includes it (no
  duplicated typedefs). Includes are emitted in deterministic order.
- String interpolation and `new` use GNU statement-expressions (`({ ... })`), so GCC is required.

## Diagnostics

Format: `file(line,col): error CODE: message`. The real source file is tracked via
`CompilerLogger.CurrentFile` (set per module by the pipeline) — never hardcode a filename.
Add every new code to `Utilities/Diagnostics/DiagnosticCatalog.cs` and `docs/diagnostics.md`
(`tests/diagnostics-json.ps1` enforces both). `--diagnostics json` emits one JSON object per
diagnostic on stdout; human output must stay unchanged. Attach related locations with
`CompilerException.WithRelated`; see `docs/diagnostics.md`.

## Graphify (semantic index)

`Graphify/` implements `sage graphify` (see `docs/graphify.md`). It reuses the lexer, parser, and
`SemanticAnalyzer` (never a second parser) and walks the analyzed AST by shape, so new AST nodes are
indexed without touching it. The index is written to `obj/graphify` (deterministic, no timestamps);
bump `GraphifyStore.SchemaVersion` when the persisted JSON changes incompatibly. Tests:
`tests/graphify-index.ps1` and `tests/graphify-query.ps1` with fixtures in `tests/fixtures/graphify`. It must never affect
`run`/`build` behavior or output.

## VS Code extension (companion repo)

The planned compiler-derived semantic navigation tool is named **Graphify**.

- Plain JavaScript, **no build step** (`extension.js`). Contributes grammar, icons, a
  "Sage: Run Current File" command, and a **Test Explorer** that runs `*.test.sg` files and parses
  `[PASS]`/`[FAIL]`.
- It invokes the compiler via the `sage.command` setting (e.g. `dotnet <path>/Sage.dll`).
- Source is published to `TheSampaio/SageExtensionCode`, branch `codex/sage-extension`.
  The remote `main` is an independent wrapper history; preserve it and use the
  nested extension's configured branch/upstream. Keep the MIT license and repository metadata current.

## Environment notes

- Windows + GCC (w64devkit-style, `gcc` on PATH). CRLF line-ending warnings from Git are benign.
