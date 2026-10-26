# Extension methods - 2026-10-26

Sage 0.34.1 adds opt-in `method func` declarations inside modules. A call
`value.name(args)` resolves statically to the single visible method whose first
parameter accepts the receiver type, then is analyzed and emitted as
`Module::name(receiver, args)`. Borrowed receivers are auto-borrowed only as the
declaration states; consuming resource receivers are rejected (`S157`).

`tests/extension-methods.ps1` covers 25 cases: runtime equivalence with qualified
calls, `&T`/`&mut T`/copy receivers, reborrows inside modules, chained and
interpolated calls, field/method name coexistence, resource receivers and
resource-returning methods, unsafe methods, and diagnostics `S153`, `S156`,
`S157`, `S158`, `S159`, plus reused `S140`, `S160`, `S164`, `S171`, `S172`,
`S175`, `S178`, `S193`. `Sandbox/tests/extension_methods.test.sg` adds 9
runtime assertions.

Debug and Release builds completed with zero warnings/errors. All 31
PowerShell scripts passed in both configurations; Sandbox passed 27 files and
212 assertions in each.
