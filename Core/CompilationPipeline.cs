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

            // Identify the native toolchain while the front end runs (used by the build cache key).
            if (config.BuildNative) NativeToolchainIdentity.Prefetch(NativeTranslationUnit.CCompiler);

            if (config.IsDebugMode)
            {
                CompilerLogger.LogInfo($"Root: {env.ProjectRoot}");
                CompilerLogger.LogInfo($"Entry: {config.InputPath}");
                CompilerLogger.LogInfo($"StdLib: {env.StdDir}");
            }

            var queue = new Queue<string>();
            var parsedModules = new Dictionary<string, ProgramNode>();
            var moduleFiles = new Dictionary<string, string>();
            var generatedCFiles = new List<string>();
            var analyzer = new SemanticAnalyzer();
            var moduleGraph = new ModuleGraph();

            queue.Enqueue(config.InputPath);

            ParseAndDiscover(queue, parsedModules, moduleFiles, moduleGraph, config, env, analyzer);
            if (config.IsDebugMode) CompilerLogger.LogInfo($"Module graph: {moduleGraph.Describe()}");
            long semanticStart = CompilerInstrumentation.Start();
            analyzer.ValidateNativeResourceContracts();
            CompilerInstrumentation.Stop("semantic", semanticStart);
            var nativeSources = NativeSourceSet.Collect(parsedModules.SelectMany(module =>
                EnumerateExternBlocks(module.Value).Select(block => (block, moduleFiles[module.Key]))));
            GenerateCode(parsedModules, generatedCFiles, config, env, analyzer);
            string? cppAdapters = GenerateCppAdapters(parsedModules, env);

            if (config.BuildNative)
            {
                var libraries = CollectLinkLibraries(parsedModules);
                return LinkNativeBinary(generatedCFiles, cppAdapters, nativeSources, libraries, config, env);
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

        /// <summary>
        /// Writes the C++ adapter translation unit when any module declares an <c>extern cpp</c> block.
        /// Returns its path, or null so C-only projects never depend on a C++ toolchain.
        /// </summary>
        private static string? GenerateCppAdapters(Dictionary<string, ProgramNode> modules, CompilationEnvironment env)
        {
            var blocks = modules.Values.SelectMany(EnumerateExternBlocks).Where(block => block.IsCpp).ToList();
            if (blocks.Count == 0) return null;
            string path = Path.Combine(env.ObjDir, CppAdapterGenerator.FileName);
            File.WriteAllText(path, CppAdapterGenerator.Generate(blocks));
            return path;
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
            Dictionary<string, string> moduleFiles,
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

                DiagnosticOutput.RegisterSource(path);
                long lexStart = CompilerInstrumentation.Start();
                var tokens = new Lexer(File.ReadAllText(path)).Tokenize();
                CompilerInstrumentation.Stop("lex", lexStart);

                if (config.IsDebugMode)
                {
                    CompilerLogger.LogStep("1. Tokenizing...");
                    File.WriteAllText(Path.Combine(env.ObjDir, moduleName + ".tok"), TokenPrinter.Print(tokens));
                    CompilerLogger.LogStep("2. Parsing...");
                }

                long parseStart = CompilerInstrumentation.Start();
                var ast = new Parser(tokens, Path.GetFileName(path)).Parse();
                CompilerInstrumentation.Stop("parse", parseStart);
                long registerStart = CompilerInstrumentation.Start();
                analyzer.RegisterModuleSymbols(ast, moduleName);
                CompilerInstrumentation.Stop("semantic", registerStart);
                parsedModules.Add(moduleName, ast);
                moduleFiles.Add(moduleName, path);
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

                long analyzeStart = CompilerInstrumentation.Start();
                analyzer.Analyze(ast);
                CompilerInstrumentation.Stop("semantic", analyzeStart);

                if (config.IsDebugMode)
                {
                    File.WriteAllText(Path.Combine(env.ObjDir, name + ".ast"), new AstPrinter().Print(ast));
                    CompilerLogger.LogStep("4. Generating C Code...");
                }

                long codegenStart = CompilerInstrumentation.Start();
                File.WriteAllText(Path.Combine(env.ObjDir, name + ".h"), new HeaderGenerator().Generate(ast));

                string cCode = new CodeGenerator().Generate(ast, name); 
                string cPath = Path.Combine(env.ObjDir, name + ".c");

                File.WriteAllText(cPath, cCode);
                CompilerInstrumentation.Stop("codegen", codegenStart);

                cFiles.Add(cPath);
            }
        }

        /// <summary>
        /// Compiles each generated C file into its own object file and links the objects into an executable binary.
        /// </summary>
        private static int LinkNativeBinary(List<string> cFiles, string? cppAdapters, NativeSourceSet nativeSources,
            HashSet<string> libraries, CompilerConfig config, CompilationEnvironment env)
        {
            if (config.IsDebugMode) CompilerLogger.LogStep("\nLinking binaries...");

            string exeName = Path.GetFileNameWithoutExtension(config.InputPath);
            if (exeName.Equals("main", StringComparison.OrdinalIgnoreCase))
                exeName = new DirectoryInfo(env.ProjectRoot).Name;

            string exePath = Path.Combine(env.BinDir, $"{exeName}.exe");
            string runtimeIncludes = Path.Combine(AppContext.BaseDirectory, "Runtime", "include");
            string[] includeDirectories = [env.ObjDir, runtimeIncludes, .. nativeSources.IncludeDirectories];

            // Native build integration point: every native source (generated C modules, declared
            // project C/C++ sources, generated C++ adapters) becomes one translation unit compiled to
            // its own object and cached by content (see Core/Build/NativeBuilder).
            var units = cFiles.Select(file => NativeTranslationUnit.ForC(file, includeDirectories)).ToList();
            units.AddRange(CollectProjectAndCppUnits(cppAdapters, nativeSources, includeDirectories, env));
            // g++ links the C++ runtime after user libraries; C-only builds never depend on it.
            bool hasCpp = units.Any(unit => unit.Compiler == NativeTranslationUnit.CppCompiler);
            var link = new NativeLinkRequest(exePath, libraries, hasCpp ? NativeTranslationUnit.CppCompiler : NativeTranslationUnit.CCompiler);

            if (config.IsDebugMode) CompilerLogger.LogInfo($"Invoking native toolchain: {link.Linker}");

            new NativeBuilder(env.ObjDir, config.ForceClean, config.IsDebugMode).Build(units, link);

            if (config.RunOnSuccess)
            {
                long runStart = CompilerInstrumentation.Start();
                int exitCode = RunApplication(exePath);
                CompilerInstrumentation.Stop("run", runStart);
                return exitCode;
            }
            else
            {
                CompilerLogger.LogSuccess($"Build complete: {exePath}");
                return 0;
            }
        }

        /// <summary>
        /// Describes declared project sources and the generated C++ adapters as translation units. Their
        /// objects live in <c>obj/</c> under unique names; C sources join the C11 build, while C++ sources
        /// and adapters use <c>g++ -std=c++17</c>. Adapters also see the project source directory, where
        /// <c>extern cpp</c> headers resolve.
        /// </summary>
        private static IEnumerable<NativeTranslationUnit> CollectProjectAndCppUnits(string? cppAdapters,
            NativeSourceSet nativeSources, IReadOnlyList<string> includeDirectories, CompilationEnvironment env)
        {
            if (cppAdapters != null)
                yield return NativeTranslationUnit.ForCpp(Path.GetFullPath(cppAdapters),
                    Path.GetFullPath(Path.ChangeExtension(cppAdapters, ".o")), [.. includeDirectories, env.SourceDir]);
            foreach (var (source, index) in nativeSources.CSources.Select((source, index) => (source, index)))
            {
                string objectPath = Path.Combine(env.ObjDir, $"sage_native_c_{index}_{Path.GetFileNameWithoutExtension(source)}.o");
                yield return new NativeTranslationUnit(Path.GetFileNameWithoutExtension(objectPath), source, objectPath,
                    NativeTranslationUnit.CCompiler, NativeTranslationUnit.CFlags, includeDirectories);
            }
            foreach (var (source, index) in nativeSources.CppSources.Select((source, index) => (source, index)))
                yield return NativeTranslationUnit.ForCpp(source,
                    Path.Combine(env.ObjDir, $"sage_native_{index}_{Path.GetFileNameWithoutExtension(source)}.o"), includeDirectories);
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
