namespace Sage.Graphify
{
    /// <summary>A source position inside a project file.</summary>
    public sealed record Site(string File, int Line, int Column);

    /// <summary>A declared symbol: module, namespace, function, struct, field, resource, variable, or extern alias.</summary>
    public sealed record SymbolMatch(string Kind, string Name, string Module, string File, int Line, int Column, string? Detail);

    /// <summary>A use of a symbol.</summary>
    public sealed record ReferenceMatch(string Kind, string Name, string Module, string File, int Line, int Column, string? In);

    /// <summary>A call site that targets a function.</summary>
    public sealed record CallerMatch(string Caller, string Callee, string Module, string File, int Line, int Column);

    /// <summary>A function called from a function, grouped over its call sites.</summary>
    public sealed record CalleeMatch(string Callee, string? Origin, bool Native, List<Site> Sites);

    /// <summary>The direct and transitive dependencies of a module and the modules that use it directly.</summary>
    public sealed record DependencyResult(string Module, List<string> Uses, List<string> Transitive, List<string> UsedBy);

    /// <summary>A resource type involved in a callable's contract.</summary>
    public sealed record ResourceRef(string Name, string Destructor);

    /// <summary>The ownership, borrowing, and safety contract of a function.</summary>
    public sealed record ContractResult(
        string Function,
        string Module,
        string File,
        int Line,
        int Column,
        string Signature,
        bool Unsafe,
        bool Extern,
        List<ParameterEntry>? Parameters,
        string Returns,
        string ReturnContract,
        List<ResourceRef>? Resources);

    /// <summary>A function listed in a module summary.</summary>
    public sealed record FunctionBrief(string Name, string Signature, int Line);

    /// <summary>A compact module summary without bodies.</summary>
    public sealed record ModuleResult(
        string Module,
        string File,
        string Origin,
        List<string>? Uses,
        List<string>? Namespaces,
        List<string>? Externs,
        List<StructEntry>? Structs,
        List<ResourceEntry>? Resources,
        List<VariableEntry>? Variables,
        List<FunctionBrief>? Functions);

    /// <summary>Focused read-only questions answered from the persisted index; never returns whole files.</summary>
    public sealed class GraphifyQueries(GraphifyIndex index)
    {
        private readonly Dictionary<string, ModuleShard> _modules = index.Shards.ToDictionary(s => s.Module, StringComparer.Ordinal);

        /// <summary>Declarations whose name equals the query, by qualified or simple name (fields need <c>Struct.field</c>).</summary>
        public List<SymbolMatch> Symbol(string name)
        {
            var matches = new List<SymbolMatch>();
            foreach (var shard in index.Shards)
            {
                if (shard.Module == name) matches.Add(new SymbolMatch("module", shard.Module, shard.Module, shard.File, 1, 1, shard.Origin));
                foreach (var ns in shard.Namespaces ?? [])
                    if (ns.Name == name) matches.Add(new SymbolMatch("namespace", ns.Name, shard.Module, shard.File, ns.Line, ns.Column, null));
                foreach (var function in shard.Functions ?? [])
                    if (Matches(function.Qualified, name))
                        matches.Add(new SymbolMatch(function.Extern ? "extern_function" : "function", function.Qualified,
                            shard.Module, shard.File, function.Line, function.Column, function.Signature));
                foreach (var structure in shard.Structs ?? [])
                {
                    if (structure.Name == name)
                        matches.Add(new SymbolMatch("struct", structure.Name, shard.Module, shard.File, structure.Line, structure.Column,
                            structure.Resource ? "resource" : structure.Copy ? "copy" : "move"));
                    foreach (var field in structure.Fields ?? [])
                        if ($"{structure.Name}.{field.Name}" == name)
                            matches.Add(new SymbolMatch("field", name, shard.Module, shard.File, field.Line, field.Column, field.Type));
                }
                foreach (var resource in shard.Resources ?? [])
                    if (resource.Name == name)
                        matches.Add(new SymbolMatch("resource", resource.Name, shard.Module, shard.File, resource.Line, resource.Column,
                            $"{resource.NativeType} drop {resource.Destructor}"));
                foreach (var variable in shard.Variables ?? [])
                    if (variable.Name == name)
                        matches.Add(new SymbolMatch(variable.Constant ? "const" : "var", variable.Name, shard.Module, shard.File,
                            variable.Line, variable.Column, variable.Type));
                foreach (var external in shard.Externs ?? [])
                    if (external.Alias == name)
                        matches.Add(new SymbolMatch("extern", external.Alias, shard.Module, shard.File, external.Line, external.Column,
                            external.Library == null ? external.Header : $"{external.Header}, {external.Library}"));
                foreach (var external in shard.Externs ?? [])
                    if ((external.Types ?? []).Contains(name))
                        matches.Add(new SymbolMatch("opaque_type", name, shard.Module, shard.File, external.Line, external.Column, external.Alias));
            }
            return matches.OrderBy(m => m.File, StringComparer.Ordinal).ThenBy(m => m.Line).ThenBy(m => m.Column)
                .ThenBy(m => m.Kind, StringComparer.Ordinal).ToList();
        }

        /// <summary>Every call, type use, or field use of a symbol.</summary>
        public List<ReferenceMatch> References(string name) => index.Shards
            .SelectMany(shard => (shard.References ?? []).Where(r => Matches(r.Name, name))
                .Select(r => new ReferenceMatch(r.Kind, r.Name, shard.Module, shard.File, r.Line, r.Column, r.In)))
            .OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Line).ThenBy(r => r.Column).ToList();

        /// <summary>Call sites of a function, with the calling function.</summary>
        public List<CallerMatch> Callers(string function) => index.Shards
            .SelectMany(shard => (shard.References ?? []).Where(r => r.Kind == "call" && Matches(r.Name, function))
                .Select(r => new CallerMatch(r.In ?? "<module>", r.Name, shard.Module, shard.File, r.Line, r.Column)))
            .OrderBy(c => c.File, StringComparer.Ordinal).ThenBy(c => c.Line).ThenBy(c => c.Column).ToList();

        /// <summary>Functions called by a function, grouped by resolved callee.</summary>
        public List<CalleeMatch> Callees(string function)
        {
            var groups = new SortedDictionary<string, CalleeMatch>(StringComparer.Ordinal);
            foreach (var shard in index.Shards)
            {
                foreach (var call in (shard.References ?? []).Where(r => r.Kind == "call" && r.In != null && Matches(r.In, function)))
                {
                    if (!groups.TryGetValue(call.Name, out var group))
                        groups[call.Name] = group = new CalleeMatch(call.Name, call.Origin, call.Native, []);
                    group.Sites.Add(new Site(shard.File, call.Line, call.Column));
                }
            }
            foreach (var group in groups.Values)
                group.Sites.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
            return groups.Values.ToList();
        }

        /// <summary>Direct, transitive, and reverse dependencies of a module; empty when the module is unknown.</summary>
        public List<DependencyResult> Dependencies(string module)
        {
            if (!_modules.TryGetValue(module, out var shard)) return [];
            var transitive = new SortedSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>((shard.Uses ?? []).Select(u => u.Module));
            while (pending.Count > 0)
            {
                string name = pending.Pop();
                if (name == module || !transitive.Add(name) || !_modules.TryGetValue(name, out var dependency)) continue;
                foreach (var use in dependency.Uses ?? []) pending.Push(use.Module);
            }
            var usedBy = index.Shards.Where(s => (s.Uses ?? []).Any(u => u.Module == module)).Select(s => s.Module)
                .OrderBy(name => name, StringComparer.Ordinal).ToList();
            var direct = (shard.Uses ?? []).Select(u => u.Module).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
            return [new DependencyResult(module, direct, transitive.ToList(), usedBy)];
        }

        /// <summary>The parameter ownership/borrow contract, safety, and resources of every function matching the name.</summary>
        public List<ContractResult> Contract(string function)
        {
            var results = new List<ContractResult>();
            foreach (var shard in index.Shards)
            {
                foreach (var entry in (shard.Functions ?? []).Where(f => Matches(f.Qualified, function)))
                {
                    var resources = ResourcesOf(entry);
                    results.Add(new ContractResult(entry.Qualified, shard.Module, shard.File, entry.Line, entry.Column, entry.Signature,
                        entry.Unsafe, entry.Extern, entry.Parameters, entry.Returns, entry.ReturnContract,
                        resources.Count == 0 ? null : resources));
                }
            }
            return results.OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Line).ToList();
        }

        /// <summary>A compact summary of a module's declarations.</summary>
        public List<ModuleResult> Module(string name)
        {
            if (!_modules.TryGetValue(name, out var shard)) return [];
            static List<T>? Some<T>(List<T>? list) => list is { Count: > 0 } ? list : null;
            return [new ModuleResult(shard.Module, shard.File, shard.Origin,
                Some((shard.Uses ?? []).Select(u => u.Module).ToList()),
                Some((shard.Namespaces ?? []).Select(n => n.Name).ToList()),
                Some((shard.Externs ?? []).Select(e => e.Alias).ToList()),
                shard.Structs, shard.Resources, shard.Variables,
                Some((shard.Functions ?? []).Select(f => new FunctionBrief(f.Qualified, f.Signature, f.Line)).ToList()))];
        }

        private List<ResourceRef> ResourcesOf(FunctionEntry function)
        {
            var types = (function.Parameters ?? []).Select(p => ModuleIndexer.TypeBaseName(p.Type))
                .Append(ModuleIndexer.TypeBaseName(function.Returns)).ToHashSet(StringComparer.Ordinal);
            return index.Shards.SelectMany(s => s.Resources ?? []).Where(r => types.Contains(r.Name))
                .Select(r => new ResourceRef(r.Name, r.Destructor)).DistinctBy(r => r.Name).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// A query matches a declared name exactly, or matches the unqualified part of a namespaced name
        /// (<c>print_line</c> finds <c>console::print_line</c>) when the query itself is unqualified.
        /// </summary>
        private static bool Matches(string candidate, string query) =>
            candidate == query || (!query.Contains("::") && candidate.EndsWith("::" + query, StringComparison.Ordinal));
    }
}
