using Sage.Ast;
using Sage.Core;
using Sage.Utilities;

namespace Sage.Graphify
{
    /// <summary>One analyzed source module of a project, ready for indexing.</summary>
    /// <param name="Name">Module name (file name without extension), as keyed by the compilation pipeline.</param>
    /// <param name="Path">Absolute source path.</param>
    /// <param name="IsStd">True when the file lives in the standard library directory.</param>
    /// <param name="Text">The source text the AST was derived from.</param>
    /// <param name="Ast">The analyzed syntax tree.</param>
    internal sealed record LoadedModule(string Name, string Path, bool IsStd, string Text, ProgramNode Ast);

    /// <summary>The result of running the compiler front end over a project without generating code.</summary>
    internal sealed record AnalyzedProject(CompilationEnvironment Environment, string EntryPath, IReadOnlyList<LoadedModule> Modules);

    /// <summary>
    /// Runs the existing lexer, parser, and semantic analyzer over a project (discovering <c>use</c> dependencies
    /// exactly like the compilation pipeline) so Graphify indexes compiler-resolved information rather than
    /// re-parsing sources. No C is generated and no files other than the index are written.
    /// </summary>
    internal static class GraphifyProject
    {
        /// <summary>Resolves the entry file from a project directory, a source file, or the current directory.</summary>
        public static string ResolveEntry(string? path)
        {
            string target = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? Directory.GetCurrentDirectory() : path);
            string entry = Directory.Exists(target) ? Path.Combine(target, "src", "main.sg") : target;
            if (!File.Exists(entry))
                throw new FileNotFoundException($"Entry point not found at '{entry}'. Pass a project directory containing src/main.sg or a .sg file.");
            return entry;
        }

        /// <summary>Parses and analyzes every module reachable from the entry point.</summary>
        /// <exception cref="CompilerException">The project has a compile error; the current file is set for reporting.</exception>
        public static AnalyzedProject Analyze(string entryPath)
        {
            TypeSystem.Reset();
            var environment = EnvironmentFactory.Create(new CompilerConfig(entryPath, false, false, false));
            var analyzer = new SemanticAnalyzer();
            var parsed = new List<(string Name, string Path, string Text, ProgramNode Ast)>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(entryPath);

            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                if (!visited.Add(path)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (known.Contains(name)) continue;

                CompilerLogger.CurrentFile = Path.GetFileName(path);
                DiagnosticOutput.RegisterSource(path);
                string text = File.ReadAllText(path);
                var ast = new Parser(new Lexer(text).Tokenize(), Path.GetFileName(path)).Parse();
                analyzer.RegisterModuleSymbols(ast);
                known.Add(name);
                parsed.Add((name, path, text, ast));
                foreach (var use in ast.Statements.OfType<UseNode>()) queue.Enqueue(ResolveModule(use, environment));
            }

            analyzer.ValidateNativeResourceContracts();
            foreach (var module in parsed)
            {
                CompilerLogger.CurrentFile = $"{module.Name}.sg";
                analyzer.Analyze(module.Ast);
            }

            string stdRoot = Path.GetFullPath(environment.StdDir);
            var modules = parsed.Select(module => new LoadedModule(module.Name, module.Path,
                IsInside(stdRoot, module.Path), module.Text, module.Ast)).ToList();
            return new AnalyzedProject(environment, entryPath, modules);
        }

        private static string ResolveModule(UseNode use, CompilationEnvironment environment)
        {
            string local = Path.Combine(environment.SourceDir, $"{use.Module}.sg");
            string std = Path.Combine(environment.StdDir, $"{use.Module}.sg");
            if (File.Exists(local)) return local;
            if (File.Exists(std)) return std;
            throw new CompilerException(use, "S007", $"Module '{use.Module}' not found.\n   Checked:\n   - {local}\n   - {std}");
        }

        private static bool IsInside(string directory, string path)
        {
            string relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
            return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }
    }
}
