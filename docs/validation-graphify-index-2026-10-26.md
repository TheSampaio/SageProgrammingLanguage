# Graphify semantic index - 2026-10-26

Sage 0.35.1 adds `sage graphify build [path] [--stats]`. It runs the existing lexer, parser, and
semantic analyzer, then persists a compiler-derived index in `obj/graphify`: a manifest and one
compact JSON shard per module (modules, `use` dependencies, functions with parameter ownership and
borrow contracts, structs with copy/resource capabilities, native resources, resolved call origins,
type and field references with locations). See [Graphify](graphify.md).

Output is deterministic and timestamp-free. Shards are keyed by compiler version and source hash and
carry a dependency hash; a rebuild rewrites only shards whose content changed, and removes shards of
deleted modules. Struct fields now carry source positions (parser change).

`tests/graphify-index.ps1` checks silent success, no C or binaries, shard content, byte-identical
rebuilds, that an unchanged project rewrites nothing, invalidation (a module and its dependents,
a leaf only, a std module), orphan removal, compile errors (human and JSON, index untouched), and
unchanged regular builds.

Debug and Release builds completed with zero warnings/errors. All 31 PowerShell scripts passed in
both configurations; Sandbox passed 26 files and 203 assertions in each.
