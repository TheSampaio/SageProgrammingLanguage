# Bounded C++ interop (v0.30.0)

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
Sage name. Overloads get distinct Sage names, and C++ overload resolution
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
- No exception unwinds into C. An escaping exception, or a member call on a null
  handle, prints `Sage C++ interop: <alias::name>: <reason>` to stderr and
  aborts the process.
- Headers resolve from the project source directory, the generated `obj/`
  directory, and the compiler runtime includes. The optional second string links
  a prebuilt library (`-l`). Projects without `extern cpp` never invoke `g++`.

## Limits (S210 unless noted)

Parameters and results may be scalars, `b8`, `str`, raw pointers to those, or a
single pointer to a class handle declared in the same block. Structs by value,
arrays, borrows, handle values or double pointers, and handles from other blocks
are rejected. Constructors must return a handle pointer; destructors take one
handle pointer and return `none`; members take the handle first. Missing
targets are syntax errors (S001); `type` declarations are not allowed in C++
blocks (S006).

Not implemented: automatic header parsing (no Clang dependency is assumed;
GCC is the supported toolchain), reference parameters, `std::string` and
container mapping, recoverable exception results, static members, operators,
inheritance-aware casts, callbacks, thread-safety metadata, and compiling
project C++ sources. Declared types are trusted ABI contracts: a wrong
declaration fails to compile in the adapter or, for conversions, behaves as
the equivalent C++ call would.
