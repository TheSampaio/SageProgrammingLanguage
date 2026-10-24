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
still pending. Native aliases remain shared across the compilation session.
The compilation session can also see transitively discovered modules; validation
of direct-import visibility remains future work.

Run `tests/module-resolution.ps1 -CompilerDll <Sage.dll>` to validate runtime
origins, forward calls, duplicate declarations, and non-callable bindings.
