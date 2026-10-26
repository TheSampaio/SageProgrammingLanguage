# Native symbol collision detection - 2026-10-26

Sage 0.31.2 reports S212 when two different declarations would link as the same
C symbol: flattened `module_function` names, C externs, and generated C++
adapter names. The check runs while module symbols are registered, so it fires
before any C or adapter is generated, including for imported and std modules.
The same C function declared through several aliases remains valid. Symbols
inside undeclared native sources or libraries are not visible; see
[the contract](cpp-interop.md).

`tests/native-symbol-collisions.ps1` checks eight same-file rejection cases
(both declaration orders, module versus module, C extern versus module and
top-level function, adapter versus function), an imported-module collision, and a
valid program with distinct names plus a repeated C function.
