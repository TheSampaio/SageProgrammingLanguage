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
5. **Native Compiler (GCC):** The native toolchain transforms the generated C code into a functional standalone binary (`.exe`).
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

Sage is memory-safe by default. The `SemanticAnalyzer` tracks an **unsafe context depth** and rejects raw pointer usage outside it:

* **Raw pointers** (`T*`, including `none*`) may only be *declared*, *cast to*, or *dereferenced/indexed* inside an `unsafe { }` block or an `unsafe func`. `str` is a first-class safe string, not a raw pointer.
* **`extern` is implicitly unsafe**: FFI signatures may traffic in raw pointers without extra annotation.
* **`new` / `delete`**: `new T { ... }` allocates on the heap and yields a *safe reference* — surface type `T`, but represented as `T*` in C (member access lowers to `->`). References carry no pointer arithmetic and are released with `delete` (→ `free`). The reference-ness of a value is tracked via the `AstNode.IsReference` annotation and `SymbolMetadata.IsReference`.
* **`const` is enforced**: reassigning a constant is a compile error (`SymbolMetadata.IsConstant`).

### Scope Management (`SymbolTable`)

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