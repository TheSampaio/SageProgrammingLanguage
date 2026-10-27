# Module dependency graph - 2026-10-26

Sage 0.36.0 records a deterministic module dependency graph from `use`
declarations (`Core/Build/ModuleGraph`). Modules keep discovery order, edges
keep declaration order, duplicate `use` declarations collapse, and cycles are
tolerated as before. `--verbose` prints the graph; normal builds stay quiet.

Debug and Release builds completed without errors. All 30 PowerShell scripts
passed in both configurations. Sandbox passed 26 files and 203 assertions in
each configuration. `tests/module-graph.ps1` builds a cyclic diamond project
twice, checks the exact printed graph, the program output, and that the graph
is not printed without `--verbose`.
