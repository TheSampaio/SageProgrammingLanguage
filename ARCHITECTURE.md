# Sage Architecture & Technical Documentation

**Sage** is a compiled (transpiled) programming language that utilizes C11 as its low-level target. Its architecture follows the classic compiler "pipeline" model, prioritizing modularity and maintainability through the **Visitor Design Pattern**.

## 1. Compiler Architecture

The Sage compilation process is divided into six sequential stages. Each stage transforms the code into a more abstract representation or one closer to the hardware.

### A. Frontend (Analysis)

1. **Lexical Analysis (Lexer):** A single-pass scanner that breaks raw source code into strictly typed `Tokens`. It handles whitespace stripping, comment removal, and literal identification.
2. **Syntactic Analysis (Parser):** A recursive descent parser that organizes tokens into a hierarchical **Abstract Syntax Tree (AST)**. It enforces operator precedence and ensures the code matches the language grammar.

### B. Middle-end (Validation)

3. **Semantic Analyzer:** The "brain" of the compiler. It validates identifier existence, manages nested scopes via the `SymbolTable`, performs strict **Type Checking**, and ensures constants are not reassigned.

### C. Backend (Synthesis)

4. **Code Generator:** Traverses the AST and emits optimized, standards-compliant C11 source code. It handles the translation of Sage features (like string interpolation) into C-compatible logic.
5. **Native Compiler (GCC):** `Core/Build/NativeBuilder` compiles each generated C module separately (`gcc -c` into `obj/<module>.o`, in parallel) and links the objects with the `extern` link libraries into a standalone binary (`.exe`). See [incremental builds](docs/incremental-build.md).
6. **Process Executor:** Manages the secure execution and cleanup of the final program within the host environment.

## 2. Project Structure

* **`/Ast`**: Defines the "backbone" of the language. Nodes are immutable data containers (e.g., `IfNode`, `BinaryExpressionNode`) that implement the `Accept` method for visitors.
* **`/Core`**: Contains the core logic: `Lexer`, `Parser`, `SemanticAnalyzer`, and `CodeGenerator`.
* **`/Enums`**: Centralizes domain constants, primarily `TokenType` and `SageType`.
* **`/Interfaces`**: Defines the `IAstVisitor<T>` contract, allowing behavioral decoupling between data and logic.
* **`/Utilities`**: Diagnostic and infrastructure tools such as `AstPrinter`, `CompilerLogger`, and `CompilerException`.

## 3. The Visitor Pattern & Extensibility

Sage uses the **Visitor Pattern** to separate the data structure (AST) from the logic that operates on it. This allows us to add new compiler passes (e.g., an LLVM backend or an Optimizer) without changing the AST nodes themselves.

### Adding New Features: The Sage Standard Workflow

1. **The Token**: Register the new token in `TokenType.cs` and map it in `Lexer.cs`.
2. **The AST Node**: Create a new node class in `/Ast` using C# primary constructors for immutability.
3. **The Visitor Contract**: Add the corresponding `Visit` method to `IAstVisitor<T>`. This ensures all visitors (CodeGen, Semantics, Printer) are updated to support the feature.
4. **The Parser**: Implement the recursive descent logic in `Parser.cs` to consume the new tokens and produce the AST node.
5. **Semantics and Generation**:
* **`SemanticAnalyzer.cs`**: Define the "rules" (e.g., a `while` condition must be a boolean).
* **`CodeGenerator.cs`**: Define the C output (e.g., how a Sage `extern` call maps to a C library).


## 4. Type System and Scoping

Sage utilizes a **Static Typing** system with a focus on ABI safety and predictable memory behavior.

### Memory & Types

* **Primitive Types**: Maps Sage types directly to C99 fixed-width types (e.g., `i32` → `int32_t`).
* **Implicit Promotion**: Automatically allows safe conversions, such as `i32` to `f64`.
* **Explicit Casting (`as`)**: Forces a type conversion, translated directly to a C-style cast for fine-grained control.
* **C Interop Aliases**: `c_int`, `c_uint`, `c_char`, `c_size_t`, `c_void`, … map onto the *native* C types (whose width is platform-defined) so FFI bindings match real C signatures. All mapping lives in `TypeSystem.ToCType` — the single source of truth.
* **Opaque Types**: `type Name;` inside an `extern` block registers a C type name (e.g. `FILE`) with no Sage-side layout; it is emitted verbatim to C and typically used through pointers (`FILE*`).

### Memory Safety Model

The current compiler restricts some raw pointer operations through an **unsafe context depth** in `SemanticAnalyzer`. It also tracks local heap-reference transfers and deletion across branches through `OwnershipState`. See [implemented memory semantics](docs/memory.md) for exact guarantees and [the target model](docs/memory-model-proposal.md) for subsequent work.

* **Raw pointers** (`T*`, including `none*`) may only be *declared*, *cast to*, or *dereferenced/indexed* inside an `unsafe { }` block or an `unsafe func`. `str` is a distinct surface type, but its generated `char*` lifetime is not checked.
* **`extern` is implicitly unsafe**: FFI signatures may traffic in raw pointers without extra annotation.
* **`new` / `delete`**: `new T { ... }` allocates on the heap and yields a reference with surface type `T`, represented as `T*` in C (member access lowers to `->`). `SymbolMetadata.Ownership` records availability, transfer, and deletion by binding identity. The analyzer snapshots and merges these states across conditional branches; the backend clears moved and deleted pointers and uses typed GCC cleanup helpers for scope exit and early return.
* **`const` is enforced**: reassigning a constant is a compile error (`SymbolMetadata.IsConstant`).

### Scope Management (`SymbolTable`)

Callable origin and module-scoping rules are documented in [module resolution](docs/modules.md).
Argument, return, and native-alias checks are documented in [function contracts](docs/function-contracts.md).
The initial borrowed-parameter model and its lifetime boundary are documented in [borrowing](docs/borrowing.md).
Recursive checks of struct and fixed-array literals are documented in [aggregate contracts](docs/aggregate-contracts.md).
Fixed-array access checks and runtime failure behavior are documented in [array bounds](docs/array-bounds.md).
Inferred copy capabilities and fixed-array value passing are documented in [value copies](docs/value-copy.md).
Explicit `extern` call contexts and the stdlib migration are documented in [native boundaries](docs/native-boundaries.md).
The post-discovery native resource declaration pass is documented in [resource contracts](docs/native-resource-contracts.md).
Resource locals share binding-identity move/branch tracking with heap owners;
typed GCC cleanup helpers call the declared destructor once for non-null handles.
Native adoption and raw-handle access are trusted unsafe boundaries. Resource
borrows reuse the lexical loan/call conflict checks and never adopt ownership.
Resource returns move a named owner, clearing it before callee cleanup; their
call results must directly initialize local owners. Resource value parameters
consume named owners; the callee adopts them into cleanup locals before the body.
Calls reject consuming/borrowing the same resource and require stable other
arguments to avoid depending on C argument evaluation order. Resource arrays and
fields remain disabled.
Resource null comparisons are validated on named owners/borrows and marked in
the AST. The C backend reads the private handle without exposing a raw pointer.
Contextual `null` does not construct resources or add a general raw-pointer null
literal. Presence arguments in consuming calls require prior local evaluation.
The bounded [byte-file stdlib API](docs/byte-files.md) owns a native stream state,
uses mutable borrows for byte I/O, and consumes ownership for fallible close.
Its native helper header ships in the compiler's `Runtime/include` directory,
resolved relative to the executable. Standard-library directories contain only `.sg`.
Bounded printable ASCII paths use `create_bytes(u8[260], u16)` with prefix
validation and native termination; legacy `create(str)` remains unsafe.
`open_read_bytes` uses the same bounded path contract for existing input files
without truncation. The native handle retains a write capability fixed by its
constructor; incompatible byte/block writes and flush are rejected before stdio.
The [byte-buffer API](docs/byte-buffers.md) uses the same resource/borrow contracts
for exclusive fixed-capacity storage. Native bounds checks expose only the
initialized prefix; push initializes before extending length, and clear hides
previous contents without freeing the allocation. No raw buffer escapes.
Block file reads replace the buffer's initialized prefix with the actual count;
block writes borrow only initialized source bytes. Per-file native transfer
flags are captured while the exclusive file borrow is held and returned as an
ordinary `ByteTransfer` value with independent count, EOF and failure fields.

The `SymbolTable` uses a `Stack<Dictionary<string, Symbol>>` to represent **Lexical Scoping**.

* **Entering a block** (`{`): Pushes a new dictionary onto the stack.
* **Exiting a block** (`}`): Pops the current scope.
* **Shadowing**: Sage naturally supports variable shadowing; the analyzer always searches for symbols from the top (deepest) scope down to the global scope.

## 5. Code Conventions (Clean Code & SOLID)

1. **Single Responsibility**: Each class has one job (e.g., `Lexer` only knows about characters and tokens).
2. **Open/Closed Principle**: The Visitor Pattern allows us to add new operations (like a Linter or Formatter) without modifying existing AST nodes.
3. **Immutability**: Once an AST node is created by the parser, its state is frozen to prevent side effects during analysis.
4. **Standardized Diagnostics**: All errors follow the `file(line,col): error CODE: Message` format for IDE compatibility (e.g., VS Code problem matchers).

© 2026 Kellvyn Sampaio — Sage Language Project
