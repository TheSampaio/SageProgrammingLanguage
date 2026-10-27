namespace Sage.Graphify
{
    // The records below are the persisted Graphify schema (docs/graphify.md). Property names serialize in camelCase,
    // in declaration order, and default values (null, false, 0) are omitted so shards stay compact and deterministic.

    /// <summary>A <c>use</c> dependency declared by a module.</summary>
    public sealed record UseEntry(string Module, int Line, int Column);

    /// <summary>A <c>module name { ... }</c> block, which namespaces the functions it contains.</summary>
    public sealed record NamespaceEntry(string Name, int Line, int Column);

    /// <summary>An <c>extern alias("header", "library") { ... }</c> block.</summary>
    public sealed record ExternEntry(string Alias, string Header, string? Library, int Line, int Column, List<string>? Types);

    /// <summary>A function parameter with the ownership or borrowing contract it imposes on callers.</summary>
    /// <param name="Contract">One of <c>copy</c>, <c>borrow</c>, <c>borrow_mut</c>, <c>consume</c>, <c>raw_pointer</c>, <c>value</c>.</param>
    public sealed record ParameterEntry(string Name, string Type, string Contract);

    /// <summary>A Sage or native function declaration.</summary>
    /// <param name="Name">Name as declared.</param>
    /// <param name="Qualified">Resolved name used by calls: <c>namespace::name</c>, <c>alias::name</c>, or the bare name.</param>
    /// <param name="Signature">Canonical one-line signature.</param>
    /// <param name="Returns">Declared return type.</param>
    /// <param name="ReturnContract"><c>none</c>, <c>owner</c> (native resource handed to the caller), or <c>value</c>.</param>
    public sealed record FunctionEntry(
        string Name,
        string Qualified,
        string? Namespace,
        int Line,
        int Column,
        string Signature,
        string Returns,
        string ReturnContract,
        bool Unsafe,
        bool Extern,
        string? Header,
        string? Library,
        List<ParameterEntry>? Parameters);

    /// <summary>A struct field.</summary>
    public sealed record FieldEntry(string Name, string Type, int Line, int Column);

    /// <summary>A struct declaration with the capabilities inferred from its composition.</summary>
    /// <param name="Copy">True when the struct is a checked value-copy type.</param>
    /// <param name="Resource">True when the struct directly or transitively owns native resources.</param>
    public sealed record StructEntry(string Name, int Line, int Column, bool Copy, bool Resource, List<FieldEntry>? Fields);

    /// <summary>A native resource declaration: <c>resource Name: NativeType drop destructor;</c>.</summary>
    public sealed record ResourceEntry(
        string Name,
        string NativeType,
        string Destructor,
        string? Header,
        string? Library,
        int Line,
        int Column);

    /// <summary>A top-level variable or constant.</summary>
    public sealed record VariableEntry(string Name, string Type, bool Constant, int Line, int Column);

    /// <summary>A use of a declared symbol.</summary>
    /// <param name="Name">Qualified name; fields are <c>Struct.field</c>.</param>
    /// <param name="Kind"><c>call</c>, <c>type</c>, or <c>field</c>.</param>
    /// <param name="In">Qualified name of the enclosing function, or null at module level.</param>
    /// <param name="Origin">Module that declares the callee, for calls.</param>
    /// <param name="Native">True for calls that cross a native extern boundary.</param>
    public sealed record ReferenceEntry(string Name, string Kind, int Line, int Column, string? In, string? Origin, bool Native);

    /// <summary>The persisted semantic index of one source module.</summary>
    /// <param name="Key">SHA-256 of the compiler version and the source text.</param>
    /// <param name="Deps">SHA-256 over the keys of every module this one depends on or references.</param>
    public sealed record ModuleShard(
        int Schema,
        string Module,
        string File,
        string Origin,
        string Key,
        string Deps,
        List<UseEntry>? Uses,
        List<NamespaceEntry>? Namespaces,
        List<ExternEntry>? Externs,
        List<StructEntry>? Structs,
        List<ResourceEntry>? Resources,
        List<VariableEntry>? Variables,
        List<FunctionEntry>? Functions,
        List<ReferenceEntry>? References);

    /// <summary>One manifest row describing a persisted shard.</summary>
    public sealed record ManifestModule(string Module, string File, string Origin, string Key, string Deps);

    /// <summary>The project-level index of shards. It never contains timestamps or machine paths.</summary>
    public sealed record IndexManifest(int Schema, string Compiler, string Entry, List<ManifestModule> Modules);
}
