# Owned byte-file validation — 2026-10-25

Sage 0.18.0 adds `byte_file` with borrowed byte I/O, positioning/flush, and
consuming fallible close. Path acquisition remains unsafe because legacy `str`
has unchecked native lifetime. Existing unsafe `file` wrappers are unchanged.
See [the precise API and limits](byte-files.md).

Debug and Release validation completed:

- Builds: zero warnings/errors. All 19 PowerShell regression scripts passed.
- Sandbox: 23 files, 154 passing assertions in each configuration. The new file
  has eight assertions covering entirely safe empty-owner operations and moves.
- Native adapter: 45 assertions built with GCC C11, `-Wall -Wextra -Werror`.
  Fault injection covers open/allocation, positioning, read, write, flush, and
  close failures; counters verify stream/state cleanup, including allocation
  rollback and no retry/double close after reported close failure.
- Sage integration: 18 assertions exercise all 256 byte values, clean EOF,
  binary output/input transitions, flush after input, and write after non-EOF
  input. PowerShell independently verifies all persisted bytes and early-return
  output, then reopens both files with exclusive access to check handle closure.
- Ten negative cases reject unsafe acquisition without context, missing/mutable
  borrows, close from a borrow/live loan, reuse/double close, anonymous producers,
  direct native calls, and implicit narrowing of an `i32` byte variable.
- The integration project uses a path containing spaces and a copied local std
  distribution. This verifies GCC's new resolved std include path for the native
  helper header. The legacy file module also compiles alongside the new module.

Initial probes failed with S007 before the module existed, then with a missing
`sage_byte_file.h` before native std header resolution was enabled. The new
compiler change adds the selected std directory to GCC includes; no parser or
ownership rule was relaxed.

Tests create/truncate only their own temporary files. Cleanup checks the resolved
test directory before recursive removal. The adapter does not use platform
`tmpfile`, input strings for byte operations, or a public raw buffer. Current
Windows GCC behavior is operationally demonstrated; other platforms, durable
storage, safe general strings/paths, detailed OS errors, and batch byte buffers
remain outside this increment.
