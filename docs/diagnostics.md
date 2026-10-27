# Compiler diagnostics (v0.35.0)

Sage reports each failure as `file(line,col): error CODE: message`. The compiler stops at the first
diagnostic, so one compilation yields at most one diagnostic (the lexer may add more before parsing
fails). Codes are stable: a code identifies one rule even when its wording is refined.

## Human and machine-readable output

The default output is the human format above. It is unchanged by the structured option, and
successful compilations print only the usual build line.

```powershell
sage --diagnostics json build          # or: sage build --diagnostics=json
sage --diagnostics json path\to\file.sg
```

`--diagnostics json` (also `--diagnostics=json`) prints one JSON object per diagnostic, one per line, to
standard output. Nothing else is printed on failure, and nothing at all on success, so the output can
be parsed line by line. `--diagnostics human` selects the default explicitly; any other value fails.
Do not combine `--diagnostics json` with `--verbose`, which prints progress text.

### Object fields

| Field | Type | Present | Meaning |
| --- | --- | --- | --- |
| `code` | string | always | Stable diagnostic code from the table below. |
| `severity` | string | always | `error` (the compiler has no warnings yet). |
| `file` | string | source diagnostics | File name exactly as in the human output. Omitted for `S900`. |
| `path` | string | when the file was read | Absolute path of that source file. |
| `line`, `column` | number | when located | 1-based position. Omitted for failures without a source location. |
| `message` | string | always | The same text as the human output. |
| `rule` | string | known codes | The language rule the code enforces. |
| `related` | array | when available | Related symbols or locations, see below. |
| `suggestion` | string | only when reliable | An actionable correction. Never speculative. |

Each `related` entry may hold `symbol`, `note`, `file`, `line`, and `column`. Ownership diagnostics
(`S140`-`S142`) point at the operation that invalidated the owner; `S172` and `S178` point at the
conflicting borrow.

```json
{"code":"S140","severity":"error","file":"main.sg","path":"C:\\proj\\src\\main.sg","line":6,"column":10,"message":"Owner 'f' was moved by passing it to 'take' at (5,5) and cannot be used here.","rule":"A moved owner cannot be used again.","related":[{"symbol":"f","note":"invalidated by passing it to 'take'","file":"main.sg","line":5,"column":5}],"suggestion":"Use the owner before it is moved, or move it only on its last use."}
```

The process exit status is unchanged: `1` for any failure, `0` otherwise.

### Editor integration

A VS Code problem matcher can read the human format with
`^(.+)\((\d+),(\d+)\): (error|warning) (S\d+): (.*)$`. Tools that want the rule, related
locations, or the absolute path should run with `--diagnostics json` and parse each stdout line.

## Ownership diagnostics

`S140` (moved), `S141` (deleted), and `S142` (moved or deleted on some path) name the operation that
invalidated the owner and where it happened, for example `passing it to 'take'`, `'delete'`,
`assignment`, `'return'`, or `initializing 'x'`.

## Code table

Every code emitted by the compiler is listed here; `tests/diagnostics-json.ps1` fails when the
source, `Utilities/Diagnostics/DiagnosticCatalog.cs`, and this table disagree. `S900` reports failures that
are not language diagnostics, such as a missing entry point or a native toolchain error.

### Lexer, parser, imports

| Code | Rule | Suggestion |
| --- | --- | --- |
| S000 | Source text may contain only recognized characters. |  |
| S001 | The source must follow the Sage grammar; a required token is missing or misplaced. |  |
| S002 | A module block may contain only functions and extern blocks. |  |
| S003 | A type name is required here. |  |
| S004 | An expression or statement cannot start with this token. |  |
| S005 | Only variables, struct fields, array elements, and resource field paths can be assigned. |  |
| S006 | An extern block may contain only func, type, and resource declarations. |  |
| S007 | Every 'use' must name a module file in the project source directory or the standard library. | Create the module file next to the importing file, or correct the module name in 'use'. |
| S010 | String interpolation braces must be balanced. |  |
| S011 | An interpolation must contain an expression. |  |
| S012 | A variable declared in the standard form must have an initializer. |  |
| S013 | A string literal must be terminated. |  |

### Names and types

| Code | Rule | Suggestion |
| --- | --- | --- |
| S101 | An initializer must have the declared variable type. |  |
| S102 | A name may be declared only once per scope. |  |
| S103 | A constant cannot be reassigned or mutated. |  |
| S104 | A condition must have type b8. | Compare the value explicitly so the condition has type b8. |
| S105 | An identifier or function must be declared before it is used. |  |
| S107 | Logical operators require b8 operands. |  |
| S108 | Arithmetic and assignment require compatible types. |  |
| S109 | Member access requires a struct value. |  |
| S110 | Member access must name a declared field. |  |
| S111 | Only arrays and raw pointers can be indexed. |  |
| S112 | An array index must be an integer. |  |

### Unsafe contexts

| Code | Rule | Suggestion |
| --- | --- | --- |
| S120 | A raw pointer in a function signature requires an unsafe function. | Declare the function 'unsafe func', or use a safe reference. |
| S121 | A raw pointer variable requires an unsafe context. | Wrap the code in an 'unsafe { }' block, or use a plain struct value or an owning std type. |
| S122 | Casting to a raw pointer requires an unsafe context. | Wrap the cast in an 'unsafe { }' block. |
| S123 | Indexing a raw pointer requires an unsafe context. | Wrap the access in an 'unsafe { }' block. |

### Allocation

| Code | Rule | Suggestion |
| --- | --- | --- |
| S130 | 'new' requires a struct type. |  |
| S131 | A struct initializer may set only declared fields. |  |
| S132 | 'delete' applies only to a reference created with 'new'. |  |
| S133 | 'new' and 'delete' are manual memory management and require an unsafe context. | Use a plain struct value, which is cleaned up automatically, or wrap the code in an 'unsafe { }' block. |

### Ownership

| Code | Rule | Suggestion |
| --- | --- | --- |
| S140 | A moved owner cannot be used again. | Use the owner before it is moved, or move it only on its last use. |
| S141 | A deleted reference cannot be used again. | Do not use the reference after 'delete'. |
| S142 | An owner that may have been moved or deleted on another path cannot be used. | Move or delete the owner on every path, or on none. |
| S143 | Resource reassignment and field replacement are limited to standalone assignments between named local owners. |  |
| S144 | A reference needs an owning or borrowing contract before it can be used in this position. |  |
| S145 | An owner declared outside a loop cannot be moved or deleted inside it. | Declare the owner inside the loop body. |
| S146 | 'new' must directly initialize a local owning binding. |  |

### Declarations

| Code | Rule | Suggestion |
| --- | --- | --- |
| S150 | A declaration name must be unique in its scope. |  |
| S151 | Only callable symbols can be called. |  |
| S152 | Redeclared native aliases must have identical signatures and native origins. |  |
| S153 | A callable is visible only in its declaring file and in files that directly 'use' it; native aliases are file-private. | Add a 'use' for the declaring module, or declare the extern binding in this file. |
| S154 | Parameter names must be unique within a function. |  |
| S156 | A dot call requires an imported 'method func' for the receiver type; fields are not callable. |  |
| S157 | A 'method func' must be a non-extern module function whose first parameter is a supported, non-consuming receiver. |  |
| S158 | A method call must resolve to exactly one visible method. | Qualify the call as 'module::name(value, ...)'. |
| S159 | Only functions declared with 'method func' can be called with dot syntax. | Call it as 'module::name(value, ...)' or declare it with 'method func'. |

### Calls and returns

| Code | Rule | Suggestion |
| --- | --- | --- |
| S160 | A call must pass exactly the declared number of arguments. |  |
| S161 | A call argument must have the declared parameter type. |  |
| S162 | A return statement must be inside a function and return the declared type. |  |
| S163 | A function with a return type must return a value on every path. | Add a return statement on every path that reaches the end of the function. |
| S164 | Unsafe and native functions can be called only from an unsafe context. | Call it inside an 'unsafe { }' block or from an 'unsafe func'. |

### Borrowing

| Code | Rule | Suggestion |
| --- | --- | --- |
| S170 | A borrowed parameter requires an explicit matching borrow argument. | Pass '&name' or '&mut name' of a named variable matching the parameter type. |
| S171 | Mutable access requires a mutable binding that is not a shared borrow. |  |
| S172 | Borrows of the same value in one call cannot conflict. |  |
| S173 | Borrows cannot be returned, stored in structs, or crossed over a native ABI yet. |  |
| S174 | A borrowed value cannot be deleted. |  |
| S175 | Only a named variable can be borrowed. |  |
| S176 | Only scalars, supported structs, and native resources can be borrowed. |  |
| S177 | Calls and mutations cannot be evaluated alongside borrowed arguments. | Evaluate the call or mutation into a local value before this call. |
| S178 | A source value cannot be accessed in a way that conflicts with a live borrow. | End the borrow's scope before accessing the source this way. |

### Arrays and initializers

| Code | Rule | Suggestion |
| --- | --- | --- |
| S180 | An array or struct initializer needs a compatible declared destination. |  |
| S181 | An array element or field value must have the declared type. |  |
| S182 | An array literal cannot have more elements than the declared length. |  |
| S183 | A struct field can be declared or initialized only once. |  |
| S185 | A constant array index must be inside the array bounds. |  |
| S187 | A fixed-array length must be positive and representable as i32. |  |
| S188 | Fixed-array value operations are limited to supported copy contracts. |  |
| S189 | An array field of a temporary value cannot be indexed or mutated directly. | Bind the containing value to a local first. |

### Native resources

| Code | Rule | Suggestion |
| --- | --- | --- |
| S190 | A native resource must declare one pointer to an opaque or native ABI type and a matching destructor. |  |
| S191 | Native resources are supported only as local owners and explicit borrows. |  |
| S192 | Resource adoption and handle exposure require unsafe and the exact native pointer type. |  |
| S193 | An owning resource result must flow directly into a local owner. | Bind the producer's result to a local before using it. |
| S194 | A call cannot combine consumed resource arguments with conflicting borrows or evaluation hazards. |  |
| S195 | Contextual null supports only == or != against a named resource owner or borrow. |  |
| S196 | A struct containing native resources must be initialized from a literal, a named owner move, or a direct owning call. |  |
| S197 | A resource array requires local zero initialization or a direct array literal. |  |

### Native interop

| Code | Rule | Suggestion |
| --- | --- | --- |
| S210 | An 'extern cpp' binding must use a supported adapter shape and type mapping. |  |
| S211 | A declared native source must exist and be a .c, .cpp, .cc, or .cxx file. |  |
| S212 | Distinct declarations must not lower to the same C symbol. | Rename one of the declarations. |
| S213 | A native parameter cannot have type 'none'. | Use 'none*' for an untyped pointer. |
| S214 | An interpolated string creates an owned 'Text' and must directly initialize a local 'Text' variable. | Bind it first: var message: Text = $"Hello {name}"; |
| S215 | An interpolated string produces 'Text', which needs the text module. | Add 'use text;' to the file. |
| S216 | Only numbers, 'b8' and 'str' values can be interpolated. | Interpolate a field or a converted value instead. |
| S217 | A format specifier must fit the interpolated value's type. | Use flags, width, precision and a conversion allowed for that type; see docs/strings.md. |

### Tooling

| Code | Rule | Suggestion |
| --- | --- | --- |
| S900 | The compiler or an external tool (file system, native toolchain) failed before producing a language diagnostic. |  |
