# Sage Programming Language

<p align="center">
<img src="Assets/Images/sage-programming-language-logo.png" alt="Sage Logo">
</p>

**Sage** is a statically typed, compiled programming language designed for high-performance applications and systems development.
The compiler is implemented in **C# (.NET 8)** and operates as a full toolchain: it **transpiles** Sage source code (`.sg`) into optimized **C11** code and then orchestrates a native C compiler (GCC) to produce a standalone **Windows Executable (`.exe`)**.

The project strictly follows **SOLID principles** and **Clean Code** architecture, utilizing the **Visitor Pattern** for AST traversal, semantic analysis, and code generation.

## Core Features

* **Native Compilation:** Handles the entire build process from source to optimized binary (`.exe`).
* **Memory Safety by Default:** Raw pointers (`T*`) are forbidden in ordinary code and only usable inside `unsafe { }` blocks (and FFI). Constants (`const`) are strictly enforced.
* **Safe Heap Allocation (`new` / `delete`):** Allocate on the heap and get a memory-safe reference — no pointer arithmetic, released explicitly with `delete`.
* **Ergonomic FFI:** `extern` blocks with C interop aliases (`c_int`, `c_size_t`, `c_char`, …) and opaque C types (`type FILE;`) that map cleanly onto real C signatures.
* **Tree-Shaking Code Gen:** Smart header management that only includes necessary C headers (`stdio.h`, `stdint.h`, etc.) based on actual code usage.
* **Modern Type System:** Explicit fixed-width types (`i32`, `f64`, `str`, `b8`) with support for **Constants** (`const`) and **Explicit Casting** (`as`).
* **Modularity:** First-class support for `module` blocks and namespaced calls (`math::sum`) to manage large codebases.
* **Developer Tooling:** Standardized **JSON** debug outputs for Tokens (`.tok.json`) and Abstract Syntax Trees (`.ast.json`).


## Dependencies

Sage acts as a transpiler (Source-to-Source), converting your `.sg` code into optimized C. To finalize the build into a native executable, it relies on an external C toolchain.

* **GCC (GNU Compiler Collection):** Required

### Linux (Debian/Ubuntu) Installation

On Debian-based systems, the easiest way to get everything you need is by installing the `build-essential` package.

1. Open your terminal.
2. Run:

```bash
sudo apt update && sudo apt install build-essential
```

3. Verify installation:

```bash
gcc --version
```

# Windows Installation (w64devkit — Recommended)

For Windows, we recommend using **w64devkit**, a portable and lightweight MinGW-w64 GCC toolchain that works directly in CMD, PowerShell, and VS Code — without MSYS2.

## 1. Download w64devkit

1. Go to:
   [https://www.mingw-w64.org/](https://www.mingw-w64.org/)

2. Navigate to:
   **Downloads → Pre-Built Toolchains**

3. Select:
   **w64devkit**

4. You will be redirected to:
   [https://github.com/skeeto/w64devkit](https://github.com/skeeto/w64devkit)

5. Open the **Releases** page.

6. Download the latest file named similar to:

```
w64devkit-<version>.zip
```

(Example: `w64devkit-1.20.0.zip`)

## 2. Extract the Toolchain

Extract the `.zip` file to a permanent location, for example:

```
C:\Program Files\w64devkit
```

After extraction, you should have:

```
C:\Program Files\w64devkit\bin\gcc.exe
```

## 3. Add GCC to the Windows PATH

To use `gcc` from CMD, PowerShell, or VS Code:

1. Press `Win + S`
2. Search for **Edit the system environment variables**
3. Click **Environment Variables**
4. Under **System variables**, select **Path**
5. Click **Edit**
6. Click **New**
7. Add:

```
C:\Program Files\w64devkit\bin
```

8. Click **OK** on all dialogs
9. Close and reopen your terminal

## 4. Verify Installation

Open a new **Command Prompt (CMD)** and run:

```cmd
gcc --version
```

If correctly installed, GCC version information will be displayed.

If you see:

```
'gcc' is not recognized as an internal or external command
```

Then:

* The PATH was not added correctly
* The terminal was not restarted
* The folder path is incorrect

## Why w64devkit?

* No MSYS2 required
* No special shell required
* Works directly in CMD and PowerShell
* Simple and lightweight
* Ideal for Sage’s C transpilation workflow

This setup allows Sage to generate C code and compile it immediately using:

```cmd
gcc output.c -o program.exe
```

Sage acts as a transpiler (Source-to-Source), converting your `.sg` code into optimized C. To finalize the build into a native executable, it relies on an external C toolchain.

* **GCC (GNU Compiler Collection):** [Official Website]() — **Required**

### Linux (Debian/Ubuntu) Installation

On Debian-based systems, the easiest way to get everything you need is by installing the `build-essential` package.

1. Open your terminal.
2. Run the following command:
```bash
sudo apt update && sudo apt install build-essential
```

3. Verify the installation by typing `gcc --version`.

### Windows (MinGW-w64) Installation

For Windows, we recommend using **MinGW-w64** via MSYS2 for a modern and stable environment.

#### 1. Install MSYS2

* Download and run the installer from [msys2.org]().
* Once finished, open the **MSYS2 UCRT64** terminal and run:
```bash
pacman -S mingw-w64-ucrt-x86_64-gcc
```

#### 2. Adding to System PATH

To run `gcc` from any terminal (PowerShell, CMD, or VS Code), you must add it to your Environment Variables:

1. Press `Win + S` and search for **"Edit the system environment variables"**.
2. Click **Environment Variables** (bottom right).
3. Under **System variables**, find the **Path** variable and click **Edit**.
4. Click **New** and paste the path to your MinGW bin folder (usually `C:\msys64\ucrt64\bin`).
5. Click **OK** on all windows.
6. **Restart your terminal** and type `gcc --version` to confirm.

## Compiler Pipeline

1. **Lexer:** Converts source code into a sequence of strictly typed `Tokens`.
2. **Parser:** Builds a hierarchical **Abstract Syntax Tree (AST)** using recursive descent.
3. **Semantic Analyzer:** Manages scopes via `SymbolTable`, validates mutability (`const`), and performs strict **Type Checking**.
4. **Code Generator:** Traverses the AST to emit optimized C11 code, handling ABI compliance and C-style type mapping.
5. **Native Toolchain:** Invokes GCC to compile the intermediate C code into a final binary.

## Syntax Example

```rust
use console;

struct Person
{
    name: str;
    age: i32;
    height: f32;
    money: f64;
    female: b8;
}

func main(): none
{
    const max: i32 = 5;

    // Loop + conditionals
    for (var i: i32 = 0; i < max; i++)
    {
        if (i % 2 == 0)
        {
            console::print_line("Even: {i}");
        }
        else
        {
            console::print_line("Odd: {i}");
        }
    }

    console::print_line("===");

    // Struct + formatted interpolation
    var person: Person = {
        name = "Alice",
        age = 30,
        height = 1.68,
        money = 1528.64,
        female = true
    };

    console::print_line("Hi, I'm {person.name}.");

    if (person.female)
    {
        console::print_line("I am female, {person.age} years old.");
    }
    else
    {
        console::print_line("I am male, {person.age} years old.");
    }

    console::print_line("Height: {person.height:2f}m");
    console::print_line("Balance: ${person.money:2f}");
}
```

## Memory Safety, `new` and FFI

Sage is **memory-safe by default**. Raw pointers (`T*`, including `none*`) cannot appear in
ordinary code — declaring, casting to, or dereferencing one is a compile error. They are only
allowed inside an `unsafe { }` block or in an `unsafe func`. FFI declarations (`extern`) are
implicitly unsafe, since they cross into C.

### Safe heap allocation: `new` / `delete`

```rust
struct Person { name: str; age: i32; }

func main(): none
{
    // 'new' allocates on the heap and yields a memory-safe reference (no pointer arithmetic).
    var hero: Person = new Person { name = "Zelda", age = 118 };
    console::print_line("{hero.name} is {hero.age}");
    delete hero; // release it explicitly
}
```

### Raw pointers require `unsafe`

```rust
unsafe
{
    var raw: none* = memory::alloc(400);
    var ptr: i32* = raw;
    ptr[0] = 42;
    memory::release(raw);
}
```

### Ergonomic FFI: interop aliases + opaque types

C interop aliases (`c_int`, `c_uint`, `c_char`, `c_size_t`, `c_void`, …) map onto the *native*
C types so bindings line up with real headers. Opaque C types are declared with `type Name;` and
referenced through pointers without needing their layout:

```rust
module file
{
    extern libc_stdio("stdio.h")
    {
        type FILE; // opaque handle provided by <stdio.h>
        func fopen(path: c_char*, mode: c_char*): FILE*;
        func fputs(text: c_char*, stream: FILE*): c_int;
        func fclose(stream: FILE*): c_int;
    }

    unsafe func open(path: str, mode: str): FILE* { return libc_stdio::fopen(path, mode); }
}
```

### Sage → C type mapping (quick reference)

| Sage            | C                | Notes                                  |
|-----------------|------------------|----------------------------------------|
| `i8..i64`/`u8..u64` | `int8_t..uint64_t` | Fixed-width primitives              |
| `f32` / `f64`   | `float` / `double` |                                        |
| `b8`            | `bool`           |                                        |
| `str`           | `char*`          | Safe string type (not a raw pointer)   |
| `none`          | `void`           |                                        |
| `none*`         | `void*`          | Raw pointer — `unsafe` only            |
| `c_int`, `c_uint`, `c_char`, `c_size_t`, `c_void`, … | `int`, `unsigned int`, `char`, `size_t`, `void`, … | FFI interop aliases (native width) |
| `type Name;`    | `Name`           | Opaque C type (from an extern header)  |

## Project Status

The project is currently in **v0.7.0 (Alpha)**.

* [x] **CLI & Project System** (`new`, `build`, `run`, `--version`)
* [x] **Variable Declarations** (`var`) & **Constants** (`const`)
* [x] **Control Flow** (`if`, `else`, `while`, `for`)
* [x] **FFI / External Functions** (`extern`)
* [x] **Header Tree-Shaking** (Normalized & Optimized C output)
* [x] **Logical & Comparison Operators** (`&&`, `||`, `!`, `==`, etc.)
* [x] **Modules & Namespaces** (`::`)
* [x] **String Interpolation** (Transpiled to `snprintf` with static buffers)
* [x] **Native Compilation** (GCC Integration)
* [x] **Structs and Custom Types**
* [x] **Arrays and Pointers**
* [x] **Memory Safety** (`unsafe` blocks gate raw pointers; `const` enforced)
* [x] **Safe Heap Allocation** (`new` / `delete` references)
* [x] **Ergonomic FFI** (`c_*` interop aliases + opaque `type` declarations)
* [x] **Modular Standard Library** (Libc decoupled from Compiler Core)
* [x] **Visitor-based Transpilation** (Full separation of Header/Logic)

## License

This project is licensed under the **Apache License 2.0**.

### Why Apache 2.0?
Sage is built for the community. This license allows you to:
* **Use for any purpose:** Commercial or private.
* **Modify and Distribute:** You can fork and change the code.
* **Attribution:** You must keep the original copyright notice and credit Kellvyn Sampaio.

© 2026 Kellvyn Sampaio — Sage Language Project