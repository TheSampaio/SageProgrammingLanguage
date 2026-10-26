# Callable resolution (v0.8.4)

Sage functions in a module are registered by their qualified name, such as
`math::sum`. Importing another module does not overwrite an unqualified global
`sum` entry. Two imported modules can each expose `value()` and be called through
their own names.

An unqualified call inside a module resolves to that module's own function when
one exists, then to a top-level function. A lexical variable with the same name
shadows a callable and produces `S151` if called. Outside the module, use its
qualified name. Forward declarations of Sage functions are registered before
semantic analysis of their bodies.

The selected origin is stored as `FunctionCallNode.ResolvedName`, appears in the
verbose AST output, and is used to emit the C function name. Code generation
therefore uses the same origin that semantic analysis validated.

Duplicate Sage callable declarations, including a collision between a native
alias and a Sage module callable, produce `S150`. Re-registering the same AST
declaration during compiler passes is idempotent.

The [function-contract increment](function-contracts.md) validates Sage signatures
and the consistency of repeated native aliases. Ownership-aware signatures are
still pending.

## Direct-import visibility (v0.34.0)

Each source file is a compilation unit named by its file stem; `use alpha;`
imports the unit `alpha.sg`. A Sage callable (module function or top-level
function) is visible only in the file that declares it and in files that
directly `use` that file. Transitive discovery grants nothing: if `main.sg` uses
`gamma` and `gamma.sg` uses `alpha`, `main.sg` must also write `use alpha;`
before calling `alpha::value()`. Native extern aliases are private to each file
that declares them; another file declares its own extern binding (identical
repeated declarations remain valid). Violations report `S153` with the declaring
file. Struct and resource type names are still shared across the import closure.

Migration: add the missing `use` directive. Sandbox `byte_file_buffer.test.sg`
and two fixtures previously reached `byte_buffer` only through `byte_file`.

## Generated C symbol collisions (v0.34.0)

Module functions lower to `module_function` C names. Two different callables
that would produce the same C symbol, such as `a_b::c` and `a::b_c`, a top-level
`alpha_value` beside `alpha::value`, a module function named `main`, or a Sage
function named like a declared native function, are rejected with `S155`
instead of colliding at link time. Several native aliases may still bind the
same C library function.

| Code | Meaning |
|------|---------|
| S150 | Duplicate Sage callable or native alias/Sage callable conflict |
| S151 | Called name is a non-callable binding |
| S153 | Callable not visible: its file is not directly imported, or a native alias belongs to another file |
| S155 | Two callables lower to the same C symbol |

[Extension methods](extension-methods.md) (v0.34.1) reuse these rules: a method
is a candidate only when its module is visible to the calling file.

Run `tests/module-resolution.ps1 -CompilerDll <Sage.dll>` to validate runtime
origins, forward calls, duplicate declarations, and non-callable bindings, and
`tests/module-visibility.ps1` for direct-import visibility and C symbol collisions.
