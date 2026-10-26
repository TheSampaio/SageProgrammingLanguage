# Graphify: compiler-derived semantic index (v0.35.1)

Graphify is the Sage developer tool for navigating a project without reading whole files. It
runs the existing lexer, parser, and semantic analyzer (no second parser) and persists what the
compiler resolved: modules, `use` dependencies, functions with their ownership and borrow
contracts, structs, native resources, call relationships, and symbol references with source
locations. It uses no AI model and costs no AI tokens; it is plain, deterministic compiler output.

## Commands

```powershell
sage graphify build [path] [--stats]
```

`path` is a project directory (containing `src/main.sg`) or a `.sg` file, and defaults to the
current directory. `build` is silent on success. `--stats` prints one JSON object:
`{"modules":4,"written":["geometry","main"],"unchanged":["console","files"],"removed":[]}`.

A project that does not compile reports the usual diagnostics (also as JSON with
`--diagnostics json`, see [diagnostics](diagnostics.md)), exits with status 1, and leaves the
previous index untouched. Indexing never generates C, links, or runs anything, and it does not change
`sage run`, `sage build`, or any other command.

## Index layout and incremental updates

The index lives in `obj/graphify/` of the project:

```text
obj/graphify/manifest.json        project entry, compiler version, one row per module
obj/graphify/modules/<name>.json  one shard per module (std modules included)
```

Output is deterministic: modules and references are sorted, there are no timestamps or machine
paths, and shards are compact JSON with default values omitted. Two builds of the same sources with
the same compiler are byte-identical. Standard-library files are reported as `std/<name>.sg`
wherever they are installed.

Each shard carries `key`, a hash of the compiler version and the module source, and `deps`, a hash of
the keys of every module it depends on: the modules reachable through `use` plus modules whose
declarations it references directly (call targets and types). A rebuild re-analyzes the project
(the compiler's symbol resolution is project-wide) but rewrites a shard only when its bytes change,
which happens exactly when its source, the compiler version, or a dependency changed. Shards of
modules that were removed from the project are deleted. Editing one module therefore rewrites that
module and the modules that depend on it; independent modules are left untouched.

## Schema

All names are camelCase; absent fields mean `false`, `0`, or an empty list. Positions are 1-based.

`manifest.json`: `schema`, `compiler`, `entry`, and `modules[]` of `{module, file, origin, key, deps}`.

Module shard:

| Field | Meaning |
| --- | --- |
| `schema`, `module`, `file`, `origin` | Layout version, module name, project-relative path, `project` or `std`. |
| `key`, `deps` | Cache keys described above. |
| `uses[]` | `{module, line, column}` per `use`. |
| `namespaces[]` | `{name, line, column}` per `module name { }` block. |
| `externs[]` | `{alias, header, library, line, column, types[]}` per extern block; `types` are opaque C types. |
| `structs[]` | `{name, line, column, copy, resource, fields[{name, type, line, column}]}`. `copy` is the checked value-copy capability; `resource` means the struct owns native resources. |
| `resources[]` | `{name, nativeType, destructor, header, library, line, column}`. `destructor` is `alias::name`. |
| `variables[]` | Top-level `{name, type, constant, line, column}`. |
| `functions[]` | `{name, qualified, namespace, line, column, signature, returns, returnContract, unsafe, extern, header, library, parameters[]}`. |
| `references[]` | `{name, kind, line, column, in, origin, native}`; see below. |

Parameter contract (`parameters[]` is `{name, type, contract}`):

| `contract` | Meaning for the caller |
| --- | --- |
| `copy` | Passed by value into independent storage (scalars, arrays, copyable structs). |
| `borrow` | `&T`: the caller keeps ownership and lends shared access for the call. |
| `borrow_mut` | `&mut T`: exclusive mutable access for the call. |
| `consume` | A native resource owner moves into the callee, which releases it. |
| `raw_pointer` | A raw pointer; the function is unsafe or native. |
| `value` | Any other by-value type, for example `str`. |

`returnContract` is `none`, `owner` (a native resource handed to the caller), or `value`. `unsafe` is
true for `unsafe func` and for every native (`extern`) function, which require an unsafe context to
call. `qualified` is the name calls resolve to: `namespace::name`, `alias::name` for native
functions, or the bare name.

References: `kind` is `call` (`name` is the resolved callee, `origin` the module that declares it,
`native` true across an extern boundary), `type` (a struct, resource, or opaque type named in a
declaration, cast, or `new`) or `field` (`name` is `Struct.field`). `in` is the enclosing function,
absent at module level. Call targets are the origins the semantic analyzer resolved, so they match
what the compiler links.

## Limits

- Local variables and parameters are not indexed as symbols; references cover callables, types, and
  struct fields.
- Type references are located at the declaration or expression that names the type, not at the type
  token; parameter types are located at the function.
- Field references need the analyzer's resolved type, so a field access through an expression whose
  struct type is unknown is skipped.
- Struct-literal field names and named-field initializers are not individual references.
- Re-analysis is project-wide on every build; only shard writes are incremental.
