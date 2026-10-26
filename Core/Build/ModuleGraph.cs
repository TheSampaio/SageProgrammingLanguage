namespace Sage.Core.Build
{
    /// <summary>
    /// Directed graph of Sage modules built from their <c>use</c> declarations.
    /// Modules keep discovery order and edges keep declaration order, so every query is
    /// deterministic. Cycles are allowed (module discovery already tolerates them);
    /// traversals simply visit each module once.
    /// </summary>
    public sealed class ModuleGraph
    {
        private readonly List<string> _modules = [];
        private readonly Dictionary<string, List<string>> _dependencies = new(StringComparer.Ordinal);

        /// <summary>Modules in discovery order.</summary>
        public IReadOnlyList<string> Modules => _modules;

        /// <summary>Registers a module. Re-registering an existing module is a no-op.</summary>
        public void AddModule(string module)
        {
            if (_dependencies.ContainsKey(module)) return;
            _modules.Add(module);
            _dependencies.Add(module, []);
        }

        /// <summary>Records that <paramref name="module"/> uses <paramref name="dependency"/>.</summary>
        public void AddDependency(string module, string dependency)
        {
            AddModule(module);
            var edges = _dependencies[module];
            if (!edges.Contains(dependency, StringComparer.Ordinal)) edges.Add(dependency);
        }

        /// <summary>Direct dependencies of a module, in declaration order.</summary>
        public IReadOnlyList<string> GetDependencies(string module) =>
            _dependencies.TryGetValue(module, out var edges) ? edges : [];

        /// <summary>Compact, deterministic text form, e.g. <c>main -&gt; alpha, beta; alpha -&gt; (none)</c>.</summary>
        public string Describe() => string.Join("; ", _modules.Select(m =>
            $"{m} -> {(GetDependencies(m).Count == 0 ? "(none)" : string.Join(", ", GetDependencies(m)))}"));
    }
}
