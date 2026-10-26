# Graphify query CLI - 2026-10-26

Sage 0.35.2 adds `sage graphify query <symbol|refs|callers|callees|deps|contract|module> <name>
[--project <path>] [--human] [--no-refresh]`. Answers are compact one-line JSON (or text with
`--human`), refresh the index incrementally first, and fall back to the last good index (flagged
`stale`) when the project does not compile. Normal `run`/`build` behavior is untouched. See
[Graphify](graphify.md).

`tests/graphify-query.ps1` checks every query kind against a fixture project (ownership and borrow
contracts, call origins, field/type references, dependency closure, std modules), answer size,
human mode, usage errors, refresh after edits, the stale fallback, `--no-refresh`, and
never-indexed projects.

Debug and Release builds completed with zero warnings/errors. All 32 PowerShell scripts passed in
both configurations; Sandbox passed 26 files and 203 assertions in each.
