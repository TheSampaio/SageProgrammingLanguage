# Validation: escaping borrows (v0.32.1, 2026-10-26)

Scope: confirm that no safe Sage construct lets a borrow outlive or escape its
owner, and close the one gap found.

Finding: a cast to a borrow type (`x as &i32`) was type-checked as a borrow
value without a named source. It could not initialize a borrowed binding or
argument (S170), but it reached code generation in operand positions such as
comparisons, fabricating a pointer from a value. Casts to `&T`/`&mut T` are now
rejected with S173 in safe and unsafe code.

Evidence: `tests/memory-escaping-borrows.ps1` (27 cases: two positive copy
controls and 25 rejected escape routes with exact diagnostic codes, each
checked to stop before C generation). Full regression passes in Debug and
Release.
