using Sage.Ast;
using Sage.Core.Build;
using Sage.Utilities;
using System.Diagnostics;

namespace Sage.Core
{
    /// <summary>
    /// Orchestrates the multi-phase compilation process, including discovery, analysis, generation, and linking.
    /// </summary>
    public static class CompilationPipeline
    {
        /// <summary>
        /// Executes the full compilation lifecycle based on provided command-line arguments.
        /// </summary>
        /// <param name="args">The command-line arguments containing paths and configuration flags.</param>
        public static int Run(string[] args)
        {
            TypeSystem.Reset();

            var config = ConfigurationResolver.Resolve(args);
            var env = EnvironmentFactory.Create(config);

            if (config.IsDebugMode)
            {
                CompilerLogger.LogInfo($"Root: {env.ProjectRoot}");
                CompilerLogger.LogInfo($"Entry: {config.InputPath}");
                CompilerLogger.LogInfo($"StdLib: {env.StdDir}");
            }

            var queue = new Queue<string>();
            var parsedModules = new Dictionary<string, ProgramNode>();
            var generatedCFiles = new List<string>();
            var analyzer = new SemanticAnalyzer();
            var moduleGraph = new ModuleGraph();

            queue.Enqueue(config.InputPath);

            ParseAndDiscover(queue, parsedModules, moduleGraph, config, env, analyzer);
            if (config.IsDebugMode) CompilerLogger.LogInfo($"Module graph: {moduleGraph.Describe()}");
            analyzer.ValidateNativeResourceContracts();
            GenerateCode(parsedModules, generatedCFiles, config, env, analyzer);

            if (config.BuildNative)
            {
                var libraries = CollectLinkLibraries(parsedModules);
                return LinkNativeBinary(generatedCFiles, libraries, config, env);
            }
            return 0;
        }

        /// <summary>
        /// Gathers the set of native libraries requested by extern blocks across all modules,
        /// so they can be forwarded to the linker (e.g. <c>-lws2_32</c>).
        /// </summary>
        private static HashSet<string> CollectLinkLibraries(Dictionary<string, ProgramNode> modules)
        {
            var libs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ast in modules.Values)
            {
                foreach (var ext in EnumerateExternBlocks(ast))
                {
                    if (!string.IsNullOrWhiteSpace(ext.Library)) libs.Add(ext.Library!);
                }
            }
            return libs;
        }

        private static IEnumerable<ExternBlockNode> EnumerateExternBlocks(ProgramNode ast)
        {
            foreach (var stmt in ast.Statements)
            {
                if (stmt is ExternBlockNode top) yield return top;
                else if (stmt is ModuleNode module)
                    foreach (var member in module.Members.OfType<ExternBlockNode>()) yield return member;
            }
        }

        /// <summary>
        /// Recursively parses source files and discovers module dependencies.
        /// </summary>
        private static void ParseAndDiscover(
            Queue<string> queue,
            Dictionary<string, ProgramNode> parsedModules,
            ModuleGraph moduleGraph,
            CompilerConfig config,
            CompilationEnvironment env,
            SemanticAnalyzer analyzer)
        {
            var visited = new HashSet<string>();

            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                if (!visited.Add(path)) continue;

                string moduleName = Path.GetFileNameWithoutExtension(path);
                if (parsedModules.ContainsKey(moduleName)) continue;

                if (config.IsDebugMode) Console.WriteLine($"\n--- [MODULE] Processing: {moduleName} ---");

                CompilerLogger.CurrentFile = Path.GetFileName(path);

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"Source file not found: {path}");
                }

                var tokens = new Lexer(File.ReadAllText(path)).Tokenize();

                if (config.IsDebugMode)
                {
                    CompilerLogger.LogStep("1. Tokenizing...");
                    File.WriteAllText(Path.Combine(env.ObjDir, moduleName + ".tok"), TokenPrinter.Print(tokens));
                    CompilerLogger.LogStep("2. Parsing...");
                }

                var ast = new Parser(tokens, Path.GetFileName(path)).Parse();
                analyzer.RegisterModuleSymbols(ast);
                parsedModules.Add(moduleName, ast);
                moduleGraph.AddModule(moduleName);

                foreach (var use in ast.Statements.OfType<UseNode>())
                {
                    ResolveDependency(use, env, queue);
                    moduleGraph.AddDependency(moduleName, use.Module);
                }
            }
        }

        /// <summary>
        /// Locates a required module in the local project or standard library and adds it to the processing queue.
        /// </summary>
        private static void ResolveDependency(UseNode useNode, CompilationEnvironment env, Queue<string> queue)
        {
            string moduleName = useNode.Module;
            string local = Path.Combine(env.SourceDir, $"{moduleName}.sg");
            string std = Path.Combine(env.StdDir, $"{moduleName}.sg");

            if (File.Exists(local))
            {
                queue.Enqueue(local);
            }
            else if (File.Exists(std))
            {
                queue.Enqueue(std);
            }
            else
            {
                throw new CompilerException(useNode, "S007",
                    $"Module '{moduleName}' not found.\n   Checked:\n   - {local}\n   - {std}");
            }
        }

        /// <summary>
        /// Performs semantic validation and generates corresponding C source and header files for each module.
        /// </summary>
        private static void GenerateCode(
            Dictionary<string, ProgramNode> modules,
            List<string> cFiles,
            CompilerConfig config,
            CompilationEnvironment env,
            SemanticAnalyzer analyzer)
        {
            // Emit the shared runtime prelude once for the whole build.
            File.WriteAllText(Path.Combine(env.ObjDir, HeaderGenerator.PreludeFileName), HeaderGenerator.GeneratePrelude());

            foreach (var (name, ast) in modules)
            {
                CompilerLogger.CurrentFile = $"{name}.sg";

                if (config.IsDebugMode)
                {
                    Console.WriteLine($"\n--- [MODULE] Finishing: {name} ---");
                    CompilerLogger.LogStep("3. Semantic Analysis...");
                }

                analyzer.Analyze(ast);

                if (config.IsDebugMode)
                {
                    File.WriteAllText(Path.Combine(env.ObjDir, name + ".ast"), new AstPrinter().Print(ast));
                    CompilerLogger.LogStep("4. Generating C Code...");
                }

                File.WriteAllText(Path.Combine(env.ObjDir, name + ".h"), new HeaderGenerator().Generate(ast));

                string cCode = new CodeGenerator().Generate(ast, name); 
                string cPath = Path.Combine(env.ObjDir, name + ".c");

                File.WriteAllText(cPath, cCode);

                cFiles.Add(cPath);
            }
        }

        /// <summary>
        /// Compiles each generated C file into its own object file and links the objects into an executable binary.
        /// </summary>
        private static int LinkNativeBinary(List<string> cFiles, HashSet<string> libraries, CompilerConfig config, CompilationEnvironment env)
        {
            if (config.IsDebugMode) CompilerLogger.LogStep("\nLinking binaries...");

            string exeName = Path.GetFileNameWithoutExtension(config.InputPath);
            if (exeName.Equals("main", StringComparison.OrdinalIgnoreCase))
                exeName = new DirectoryInfo(env.ProjectRoot).Name;

            string exePath = Path.Combine(env.BinDir, $"{exeName}.exe");
            string runtimeIncludes = Path.Combine(AppContext.BaseDirectory, "Runtime", "include");
            string[] includeDirectories = [env.ObjDir, runtimeIncludes];

            // Native build integration point: every native source (generated C modules today,
            // other C/C++ sources later) becomes one translation unit compiled to obj/<name>.o
            // and cached by content (see Core/Build/NativeBuilder).
            var units = cFiles.Select(file => NativeTranslationUnit.ForC(file, includeDirectories)).ToList();
            var link = new NativeLinkRequest(exePath, libraries);

            if (config.IsDebugMode) CompilerLogger.LogInfo("Invoking native toolchain: gcc");

            new NativeBuilder(env.ObjDir, config.ForceClean, config.IsDebugMode).Build(units, link);

            if (config.RunOnSuccess)
            {
                return RunApplication(exePath);
            }
            else
            {
                CompilerLogger.LogSuccess($"Build complete: {exePath}");
                return 0;
            }
        }

        /// <summary>
        /// Starts the compiled application and monitors its exit status.
        /// </summary>
        private static int RunApplication(string exePath)
        {
#if DEBUG
            CompilerLogger.LogStep("Running Application...");
            Console.WriteLine("--------------------------------------------------");
#endif
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"Executable not found at: {exePath}");
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            }) ?? throw new InvalidOperationException($"Could not start application: {exePath}");

            process.WaitForExit();
#if DEBUG
            Console.WriteLine("--------------------------------------------------");
            CompilerLogger.LogInfo($"App exited with code: {process.ExitCode}");
#endif
            return process.ExitCode;
        }
    }
}
