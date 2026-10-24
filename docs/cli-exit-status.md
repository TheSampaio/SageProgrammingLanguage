# CLI exit status (v0.9.3)

`sage run` and `sage path/to/file.sg` return the executed application's native
exit status. For example, a trusted native call that terminates the program with
status 37 makes the Sage CLI return 37, even when compilation succeeded. A normal
successful program returns zero. Debug and Release follow the same rule.

`sage build` compiles and links without executing the application and returns
zero on success. Version and project-initialization commands also return zero
on success. Compilation, missing input, linking, or process-start failures return
one through the existing diagnostic handler.

This lets scripts and the VS Code extension detect runtime failures using the
process result. It does not convert native termination into a recoverable Sage
error or unwind owned resources when the entire process exits. Test assertion
markers remain a separate test framework protocol.

Validate with `tests/cli-exit-codes.ps1 -CompilerDll <Sage.dll>`, which checks
version reporting, unresolved imports, explicit-file/run status propagation,
build-only behavior, and successful execution.
