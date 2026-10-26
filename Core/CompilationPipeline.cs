using Sage.Ast;
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
            var moduleFiles = new Dictionary<string, string>();
            var generatedCFiles = new List<string>();
            var analyzer = new SemanticAnalyzer();

            queue.Enqueue(config.InputPath);

            ParseAndDiscover(queue, parsedModules, moduleFiles, config, env, analyzer);
            analyzer.ValidateNativeResourceContracts();
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
                analyzer.RegisterModuleSymbols(ast, moduleName);
                parsedModules.Add(moduleName, ast);
                moduleFiles.Add(moduleName, path);

                foreach (var use in ast.Statements.OfType<UseNode>())
                {
                    ResolveDependency(use, env, queue);
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
        /// Invokes the native toolchain to link generated C files into an executable binary.
        /// </summary>
        private static int LinkNativeBinary(List<string> cFiles, string? cppAdapters, NativeSourceSet nativeSources,
            HashSet<string> libraries, CompilerConfig config, CompilationEnvironment env)
        {
            if (config.IsDebugMode) CompilerLogger.LogStep("\nLinking binaries...");

            string exeName = Path.GetFileNameWithoutExtension(config.InputPath);
            if (exeName.Equals("main", StringComparison.OrdinalIgnoreCase))
                exeName = new DirectoryInfo(env.ProjectRoot).Name;

            string exePath = Path.Combine(env.BinDir, $"{exeName}.exe");
            string sources = string.Join(" ", cFiles.Concat(nativeSources.CSources).Select(f => $"\"{Path.GetFullPath(f)}\""));
            string libFlags = libraries.Count > 0
                ? " " + string.Join(" ", libraries.OrderBy(l => l, StringComparer.Ordinal).Select(l => $"-l{l}"))
                : "";
            string runtimeIncludes = Path.Combine(AppContext.BaseDirectory, "Runtime", "include");
            string includes = $" -I\"{env.ObjDir}\" -I\"{runtimeIncludes}\"" +
                string.Concat(nativeSources.IncludeDirectories.Select(directory => $" -I\"{directory}\""));
            string cppObjects = CompileCppSources(cppAdapters, nativeSources, includes, config, env);
            // The C++ runtime follows user libraries so their C++ dependencies resolve too.
            string cppRuntime = cppObjects.Length > 0 ? " -lstdc++" : "";
            string args = $"{sources}{cppObjects} -o \"{exePath}\" -std=c11{includes}{libFlags}{cppRuntime}";

            if (config.IsDebugMode) CompilerLogger.LogInfo("Invoking native toolchain: gcc");

            ProcessExecutor.Run("gcc", args);

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
        /// Compiles the generated adapters and declared C++ sources with g++, one object each, and
        /// returns them as link arguments. Adapters also see the project source directory, where
        /// <c>extern cpp</c> headers resolve. Returns an empty string when the build has no C++.
        /// </summary>
        private static string CompileCppSources(string? cppAdapters, NativeSourceSet nativeSources, string includes,
            CompilerConfig config, CompilationEnvironment env)
        {
            var units = nativeSources.CppSources
                .Select((source, index) => (Source: source, Object: Path.Combine(env.ObjDir, $"sage_native_{index}_{Path.GetFileNameWithoutExtension(source)}.o"), Includes: includes))
                .ToList();
            if (cppAdapters != null)
                units.Insert(0, (cppAdapters, Path.ChangeExtension(cppAdapters, ".o"), $"{includes} -I\"{env.SourceDir}\""));
            if (units.Count == 0) return "";

            if (config.IsDebugMode) CompilerLogger.LogInfo("Invoking native toolchain: g++");
            foreach (var (source, objectFile, unitIncludes) in units)
                ProcessExecutor.Run("g++", $"-c \"{Path.GetFullPath(source)}\" -o \"{Path.GetFullPath(objectFile)}\" -std=c++17{unitIncludes}");
            return string.Concat(units.Select(unit => $" \"{Path.GetFullPath(unit.Object)}\""));
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
