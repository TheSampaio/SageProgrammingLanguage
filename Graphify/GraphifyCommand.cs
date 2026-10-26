using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sage.Utilities;

namespace Sage.Graphify
{
    /// <summary>The compact JSON envelope of every query answer.</summary>
    /// <param name="Query">The query kind.</param>
    /// <param name="Name">The name that was asked about.</param>
    /// <param name="Count">Number of results.</param>
    /// <param name="Results">The focused answers; empty when nothing matched.</param>
    /// <param name="Stale">True when the project no longer compiles and the answer comes from the last good index.</param>
    public sealed record QueryAnswer(string Query, string Name, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int Count, IReadOnlyList<object> Results, bool Stale);

    /// <summary>
    /// The <c>sage graphify</c> command line: <c>build</c> persists the semantic index and <c>query</c> answers focused
    /// questions about it. It runs the compiler front end only and never touches normal build or run behavior.
    /// </summary>
    public static class GraphifyCommand
    {
        private static readonly string[] QueryKinds = ["symbol", "refs", "callers", "callees", "deps", "contract", "module"];

        private const string Usage = """
            Usage:
              sage graphify build [path] [--stats]
              sage graphify query <symbol|refs|callers|callees|deps|contract|module> <name> [--project <path>] [--human] [--no-refresh]
            'path' is a project directory (containing src/main.sg) or a .sg file; it defaults to the current directory.
            """;

        /// <summary>Runs the command and returns the process exit status.</summary>
        /// <param name="args">Arguments after <c>graphify</c>.</param>
        public static int Run(string[] args)
        {
            if (args.Length == 0) return Fail("Missing graphify command.");
            return args[0].ToLowerInvariant() switch
            {
                "build" => RunBuild(args[1..]),
                "query" => RunQuery(args[1..]),
                _ => Fail($"Unknown graphify command '{args[0]}'.")
            };
        }

        private static int RunBuild(string[] args)
        {
            var options = Parse(args, valueOptions: ["--project"], flags: ["--stats"]);
            if (options.Error != null) return Fail(options.Error);
            if (options.Positional.Count > 1) return Fail("'build' takes at most one path.");

            string entry = GraphifyProject.ResolveEntry(options.Value("--project") ?? options.Positional.FirstOrDefault());
            var result = GraphifyStore.Build(entry);
            if (options.Has("--stats"))
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    modules = result.Index.Shards.Count,
                    written = result.Written,
                    unchanged = result.Unchanged,
                    removed = result.Removed
                }, GraphifyJson.Options));
            return 0;
        }

        private static int RunQuery(string[] args)
        {
            var options = Parse(args, valueOptions: ["--project"], flags: ["--human", "--no-refresh"]);
            if (options.Error != null) return Fail(options.Error);
            if (options.Positional.Count != 2) return Fail("'query' needs a query kind and a name.");
            string kind = options.Positional[0].ToLowerInvariant();
            string name = options.Positional[1];
            if (!QueryKinds.Contains(kind)) return Fail($"Unknown query '{kind}'. Expected one of: {string.Join(", ", QueryKinds)}.");

            string entry = GraphifyProject.ResolveEntry(options.Value("--project"));
            var (index, stale) = LoadIndex(entry, refresh: !options.Has("--no-refresh"));
            if (index == null) return Fail("No Graphify index exists for this project and it could not be built. Run 'sage graphify build'.");

            var queries = new GraphifyQueries(index);
            IReadOnlyList<object> results = kind switch
            {
                "symbol" => queries.Symbol(name),
                "refs" => queries.References(name),
                "callers" => queries.Callers(name),
                "callees" => queries.Callees(name),
                "deps" => queries.Dependencies(name),
                "contract" => queries.Contract(name),
                _ => queries.Module(name)
            };
            var answer = new QueryAnswer(kind, name, results.Count, results, stale);
            Console.WriteLine(options.Has("--human") ? GraphifyFormatter.Format(answer) : JsonSerializer.Serialize(answer, GraphifyJson.Options));
            return 0;
        }

        /// <summary>
        /// Refreshes the index so answers match the sources. When the project does not compile, falls back to the last
        /// good index (flagged stale) and reports the compile error on standard error.
        /// </summary>
        private static (GraphifyIndex? Index, bool Stale) LoadIndex(string entry, bool refresh)
        {
            if (!refresh) return (GraphifyStore.Load(entry), false);
            try { return (GraphifyStore.Build(entry).Index, false); }
            catch (Exception ex) when (ex is CompilerException or FileNotFoundException)
            {
                var previous = GraphifyStore.Load(entry);
                if (previous == null) throw;
                Console.Error.WriteLine(DiagnosticOutput.IsJson
                    ? DiagnosticOutput.ToJson(DiagnosticOutput.FromException(ex, CompilerLogger.CurrentFile))
                    : $"graphify: index is stale because the project does not compile: {ex.Message}");
                return (previous, true);
            }
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine($"graphify: {message}");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        private static Options Parse(string[] args, string[] valueOptions, string[] flags)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (valueOptions.Contains(arg))
                {
                    if (i + 1 >= args.Length) return new Options { Error = $"Option '{arg}' needs a value." };
                    options.Values[arg] = args[++i];
                }
                else if (flags.Contains(arg)) options.Flags.Add(arg);
                else if (arg.StartsWith("--", StringComparison.Ordinal)) return new Options { Error = $"Unknown option '{arg}'." };
                else options.Positional.Add(arg);
            }
            return options;
        }

        private sealed class Options
        {
            public List<string> Positional { get; } = [];
            public Dictionary<string, string> Values { get; } = [];
            public HashSet<string> Flags { get; } = [];
            public string? Error { get; init; }
            public bool Has(string flag) => Flags.Contains(flag);
            public string? Value(string option) => Values.GetValueOrDefault(option);
        }
    }

    /// <summary>Renders query answers as compact plain text for people.</summary>
    internal static class GraphifyFormatter
    {
        public static string Format(QueryAnswer answer)
        {
            var text = new StringBuilder();
            if (answer.Stale) text.AppendLine("(stale: the project does not compile; showing the last good index)");
            if (answer.Count == 0) text.AppendLine($"No {answer.Query} results for '{answer.Name}'.");
            foreach (object result in answer.Results) text.AppendLine(Describe(result));
            return text.ToString().TrimEnd();
        }

        private static string Describe(object result) => result switch
        {
            SymbolMatch s => $"{s.File}:{s.Line}:{s.Column} {s.Kind} {s.Name}" + (s.Detail == null ? "" : $"  {s.Detail}"),
            ReferenceMatch r => $"{r.File}:{r.Line}:{r.Column} {r.Kind} {r.Name}" + (r.In == null ? "" : $"  in {r.In}"),
            CallerMatch c => $"{c.File}:{c.Line}:{c.Column} {c.Caller} -> {c.Callee}",
            CalleeMatch c => $"{c.Callee}" + (c.Origin == null ? "" : $"  [{c.Origin}{(c.Native ? ", native" : "")}]") +
                "  " + string.Join(", ", c.Sites.Select(site => $"{site.File}:{site.Line}:{site.Column}")),
            DependencyResult d => $"{d.Module}\n  uses: {Join(d.Uses)}\n  transitive: {Join(d.Transitive)}\n  used by: {Join(d.UsedBy)}",
            ContractResult c => DescribeContract(c),
            ModuleResult m => DescribeModule(m),
            _ => result.ToString() ?? ""
        };

        private static string DescribeContract(ContractResult contract)
        {
            var text = new StringBuilder($"{contract.File}:{contract.Line}:{contract.Column} {contract.Signature}");
            if (contract.Unsafe) text.Append("\n  requires an unsafe context").Append(contract.Extern ? " (native)" : "");
            foreach (var parameter in contract.Parameters ?? [])
                text.Append($"\n  {parameter.Name}: {parameter.Type}  [{parameter.Contract}]");
            text.Append($"\n  returns {contract.Returns}  [{contract.ReturnContract}]");
            foreach (var resource in contract.Resources ?? []) text.Append($"\n  resource {resource.Name} drop {resource.Destructor}");
            return text.ToString();
        }

        private static string DescribeModule(ModuleResult module)
        {
            var text = new StringBuilder($"{module.Module}  {module.File}  [{module.Origin}]");
            if (module.Uses != null) text.Append($"\n  uses: {Join(module.Uses)}");
            if (module.Namespaces != null) text.Append($"\n  namespaces: {Join(module.Namespaces)}");
            if (module.Externs != null) text.Append($"\n  externs: {Join(module.Externs)}");
            foreach (var structure in module.Structs ?? [])
                text.Append($"\n  struct {structure.Name} ({(structure.Resource ? "resource" : structure.Copy ? "copy" : "move")}) {{ " +
                    string.Join(", ", (structure.Fields ?? []).Select(f => $"{f.Name}: {f.Type}")) + " }");
            foreach (var resource in module.Resources ?? [])
                text.Append($"\n  resource {resource.Name}: {resource.NativeType} drop {resource.Destructor}");
            foreach (var variable in module.Variables ?? [])
                text.Append($"\n  {(variable.Constant ? "const" : "var")} {variable.Name}: {variable.Type}");
            foreach (var function in module.Functions ?? []) text.Append($"\n  {function.Line}: {function.Signature}");
            return text.ToString();
        }

        private static string Join(List<string> items) => items.Count == 0 ? "-" : string.Join(", ", items);
    }
}
