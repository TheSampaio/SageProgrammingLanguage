# Structured diagnostics - 2026-10-26

Sage 0.35.0 adds opt-in machine-readable diagnostics. `--diagnostics json` prints one JSON
object per diagnostic (code, severity, file, absolute path, line, column, message, violated
rule, related symbols/locations, and a suggestion only when it is reliable) and nothing else;
successful builds stay silent. The default human format is unchanged. Non-language failures
(missing entry point, native toolchain errors) use the new `S900` code without a file.

Ownership diagnostics `S140`-`S142` now name the invalidating operation and its location
("was moved by passing it to 'take' at (5,5)") and carry it as a related location; `S172` and
`S178` carry the conflicting borrow. `docs/diagnostics.md` lists every code with its rule and
suggestion; `tests/diagnostics-json.ps1` fails when the source, `DiagnosticCatalog`, and the
docs table disagree.

Debug and Release builds completed with zero warnings/errors. All 30 PowerShell scripts
passed in both configurations; Sandbox passed 26 files and 203 assertions in each.
`tests/diagnostics-json.ps1` parses each output line as JSON and checks fields, both flag
spellings, parser/lexer/import/tool diagnostics, byte-identical human output, silent success,
and catalog/doc synchronization.
