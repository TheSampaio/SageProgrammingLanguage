# Bounded C++ interop (v0.31.3)

Sage keeps its C backend. An `extern cpp` block declares the C++ API it needs;
the compiler generates `extern "C"` adapters in `obj/sage_cpp_adapters.cpp`,
compiles them with `g++ -std=c++17`, and links the C++ runtime. C modules
never include the C++ header: they see incomplete handle types and adapter
prototypes only.

```sage
extern cpp geo("counter.hpp") {
    class Counter = demo::Counter;                       // opaque class handle
    func add(a: c_int, b: c_int): c_int = demo::add;     // free function
    func add_real(a: c_double, b: c_double): c_double = demo::add;  // overload
    func biggest(a: c_int, b: c_int): c_int = "demo::biggest<int>"; // template instance
    func create(start: c_int): Counter* = new;            // new demo::Counter(start)
    func destroy(counter: Counter*): none = delete;       // delete; null is ignored
    func increment(counter: Counter*, by: c_int): c_int = .increment; // member call
    resource OwnedCounter: Counter* drop destroy;         // ownership-aware handle
}
```

Every function names its target explicitly; nothing is inferred from the
Sage name. Static member functions bind as qualified functions
(`= demo::Math::triple`). Overloads get distinct Sage names, and C++ overload resolution
selects the target from the declared argument types. If no exact overload
exists, standard C++ implicit conversions apply. A string target is
required for explicit template instances and is restricted to qualified
names with an optional template argument list.

Calls are native calls and require `unsafe`. Combine class handles with
`resource` declarations and borrowed wrappers (see `tests/fixtures/cpp-interop.sg`)
so safe code owns handles, borrows them for member calls, and releases them
through automatic cleanup.

## Adapter guarantees

- C linkage with symbols `sage_cpp_fn_<len><alias>_<name>`; identical bindings
  shared by several modules produce one adapter (S152 rejects conflicts).
- Class handles cross as incomplete C structs and are converted with
  `reinterpret_cast`; C++ object layout is never exposed to Sage.
- No exception unwinds into C. By default an escaping exception, or a member call
  on a null handle, prints `Sage C++ interop: <alias::name>: <reason>` to stderr
  and aborts the process. A binding can opt in to recoverable failures (below).
- Headers resolve from the project source directory, the generated `obj/`
  directory, the compiler runtime includes, and directories of declared native
  sources. The optional second string links a prebuilt library (`-l`).
  Projects without C++ adapters or C++ sources never invoke `g++`.

## Recoverable exceptions (v0.31.1)

Abort stays the default. A binding opts in by ending with `catch`; the adapter
then contains the exception, records it, and returns a zero result (`0`, `false`,
or a null pointer; nothing for `none`). Two failure queries, declared in the same
block, expose the outcome of the latest `catch` call on the calling thread:

```sage
extern cpp safe("safe.hpp") {
    class Gate = safe::Gate;
    func divide(a: c_int, b: c_int): c_int = safe::divide catch;
    func open(limit: c_int): Gate* = new catch;             // null when the constructor throws
    func take(gate: Gate*, n: c_int): c_int = .take catch;  // a null handle is a failure, not an abort
    func close(gate: Gate*): none = delete;
    func failed(): b8 = catch;                              // did the latest catch call fail?
    func reason(): str = catch.message;                     // "alias::name: what()" or ""
    resource OwnedGate: Gate* drop close;
}
```

Every `catch` call resets the status first, so query it right after the call.
A failure reports the Sage binding name and `what()` (`unknown C++ exception` for
non-`std::exception` throws); the message is truncated to 255 bytes, lives in
fixed thread-local storage, and is valid until the next `catch` call on that
thread. `catch` is a reserved C++ word, so `= catch` and `= catch.message`
cannot collide with a target name. `failed` takes no parameters and returns
`b8`; `reason` takes none and returns `str` (S210 otherwise). Failure queries are
native calls and require `unsafe` like the rest. Adapters without `catch`
bindings carry no failure state. Declaring the same alias function once with and
once without `catch` is a native contract conflict (S152). A failed constructor
returns a null handle, which resource owners treat as absent (`owner == null`).
Throwing destructors are contained like any other call; `catch` on `= delete`
records the failure.

## Project native sources (v0.31.0)

Any extern block, C or C++, may declare implementation files compiled and
linked with the program:

```sage
extern cpp lib("tally.hpp") {
    source "tally.cpp";          // compiled by g++ -std=c++17 into obj/
    class Tally = lib::Tally;
    func create(start: c_int): Tally* = new;
}
extern report("report.h") {
    source "report.c";           // joins the gcc -std=c11 build
    func report_value(label: c_char*, value: c_int): none;
}
```

Paths are relative to the declaring `.sg` file, so imported and std modules
carry their own sources. Supported extensions are `.c`, `.cpp`, `.cc`, and
`.cxx`; a missing file or other extension is S211, reported before code
generation. A file declared by several blocks is compiled once, and each
source directory joins the include path. Any C++ unit (a source or the
generated adapters) makes `g++` the link driver, so the C++ runtime follows
user libraries. Since v0.37.3 each source and the adapters are cached
translation units of the [incremental build](incremental-build.md): they are
recompiled only when their content, included headers, flags, or toolchain
change. Per-source compiler flags are not configurable yet. Native symbols share the C namespace with
flattened Sage names (`module_function`); declared collisions are S212 (below).

## Native symbol collisions (v0.31.2)

Sage functions link as `module_function` (or the plain name at top level), C
externs keep their C name, and C++ adapters use `sage_cpp_fn_<len><alias>_<name>`.
When two different declarations would produce the same C symbol, the compiler
stops with S212 before generating C. Examples: `helper::twice` next to a
`helper_twice` function, `a_b::c` next to `a::b_c`, a C extern named like a Sage
function, or a Sage function named like an adapter. The same C function declared
through several aliases is one symbol and is accepted. Only declared symbols are
visible: names defined inside project `.c`/`.cpp` sources or linked libraries are
not parsed, so a collision with an undeclared native definition still surfaces as
a link error.

## Limits (S210 unless noted)

Parameters and results may be scalars, `b8`, `str`, raw pointers to those, or a
single pointer to a class handle declared in the same block. Structs by value,
arrays, borrows, handle values or double pointers, and handles from other blocks
are rejected. Constructors must return a handle pointer; destructors take one
handle pointer and return `none`; members take the handle first. Missing
targets are syntax errors (S001); `type` declarations are not allowed in C++
blocks (S006).

Not implemented: automatic header parsing (see the decision below), reference
parameters, `std::string` and container mapping, operators, inheritance-aware
casts, callbacks, thread-safety metadata, and per-source
compiler flags. Declared types are trusted ABI contracts: a wrong
declaration fails to compile in the adapter or, for conversions, behaves as
the equivalent C++ call would.

## Decision: Clang-based header parsing is deferred

Investigated 2026-10-26: no `clang`, `clang++`, or `llvm-config` is installed on
the development machine (GCC 15.2.0 is the only toolchain), and Sage requires
nothing beyond GCC. Automatic wrapper generation would need libclang or a
`clang -Xclang -ast-dump=json` pipeline: a heavyweight mandatory dependency for a
feature most projects never use. It would also have to guess what C++ headers do
not state (ownership, destruction rules, exception behavior, thread safety,
template instantiation), which the language requires to be explicit. Adapters
therefore stay generated from explicit `extern cpp` declarations that name every
target. A future parser should be an optional tool that emits those same
declarations for review, never a build dependency; C-only and explicitly declared
C++ projects keep building with GCC alone. `tests/cpp-toolchain-isolation.ps1`
puts a failing `g++` shim first on `PATH` and proves that projects without C++
(std modules, C externs, C sources, a C block named `cpp`) never invoke it, while
an `extern cpp` project does.
