# CLAUDE.md — Working rules for the Sage project

Guidance for AI assistants (and humans) working in this repository. Read this before making changes.

## What this is

Sage is a statically typed, **memory-safe** programming language that **transpiles to C11** and
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
- `new T { ... }` allocates on the heap and yields a memory-safe **reference** (surface type `T`,
  represented as `T*` in C with `->` access; no pointer arithmetic). Freed with `delete` (→ `free`).
  Reference-ness is carried by `AstNode.IsReference` and `SymbolMetadata.IsReference`.
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
  (see `Sandbox/std/server.sg`).
- Wrappers that traffic in raw pointers must be `unsafe func` (see `Sandbox/std/memory.sg`).

## Generated C

- A single shared prelude `sage.h` holds the type aliases; every module header includes it (no
  duplicated typedefs). Includes are emitted in deterministic order.
- String interpolation and `new` use GNU statement-expressions (`({ ... })`), so GCC is required.

## Diagnostics

Format: `file(line,col): error CODE: message`. The real source file is tracked via
`CompilerLogger.CurrentFile` (set per module by the pipeline) — never hardcode a filename.

## VS Code extension (companion repo)

- Plain JavaScript, **no build step** (`extension.js`). Contributes grammar, icons, a
  "Sage: Run Current File" command, and a **Test Explorer** that runs `*.test.sg` files and parses
  `[PASS]`/`[FAIL]`.
- It invokes the compiler via the `sage.command` setting (e.g. `dotnet <path>/Sage.dll`).
- The repo is initialized locally but **not yet pushed to GitHub** (no `gh` on this machine). When
  publishing, keep it MIT-licensed and update `repository.url` in `package.json`.

## Environment notes

- Windows + GCC (w64devkit-style, `gcc` on PATH). CRLF line-ending warnings from Git are benign.
