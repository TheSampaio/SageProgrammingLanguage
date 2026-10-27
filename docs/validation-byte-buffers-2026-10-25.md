# Sage 0.21.0 owned byte-buffer validation

Validated on Windows, 2026-10-25:

- Debug and Release builds: zero warnings/errors. All 22 PowerShell scripts
  passed; Sandbox passed 24 test files / 188 assertions in each configuration.
- `tests/byte-buffer.ps1`: 31 native assertions under C11 with
  `-Wall -Wextra -Werror`; 31 Sage API assertions, eight file-integration
  assertions, six cleanup assertions and ten rejected invalid programs.
- Native checks cover null owners, valid zero-capacity allocation, allocation
  failure, overflow/oversized capacity before allocation, maximum-capacity
  allocation failure without making a huge allocation, all 256 bytes, initialized
  prefix bounds, full-buffer failure without mutation, replacement, clear/reuse,
  and cleanup. Allocator poisoning checks that unused capacity stays unreadable.
- A test-only adapter logs actual allocations/frees from generated translation
  units. Owning returns, local moves, early return, consuming parameters,
  parameter returns, block scope and const ownership produce six allocations and
  six matching frees with no unknown/double release or live allocation at exit.
  This adapter is outside std and does not add production hooks.
- Safe ByteBuffer/ByteFile integration roundtrips all 256 bytes. PowerShell
  independently checks the persisted file and exclusive reopen after close.
- Negative programs fail before C generation: missing borrow, shared mutation,
  use after release/move, consuming a borrow/live loan, anonymous release,
  direct native calls without unsafe, implicit wide capacity conversion, and
  private storage access.
- Debug/Release relocation checks and a Release publish use the separate
  `Runtime/include/sage_byte_buffer.h`; copied std directories contain only `.sg`.
- The runnable `Sandbox/examples/owned_byte_buffer.sg` reports `Buffer value: 42`
  in both compiler configurations.
- Installed/reloaded extension 0.9.0 in the existing VS Code Sandbox window.
  Run Current File reports `Buffer value: 42`; a temporary use-after-release
  probe reports S140 at `(2,117)`, move location `(2,67)`, exit 1. The probe was
  closed and removed. Test Explorer passed 212/212 (24 file results + 188 asserts).
- Extension JavaScript check, 17 Node tests and VSIX packaging passed (13 files,
  52.46 KB). Material Icon Theme remains selected; no replacement theme or theme
  configuration change was added.

No parser, code-generator, or ownership check was relaxed. The new standard
module uses existing native-resource and borrowing contracts. Storage has fixed
capacity; only pushed bytes are initialized and accessible. Clear hides data but
does not securely erase it. This increment does not provide slices, resizing,
bulk I/O, owned text, general paths, or complete language memory safety.
