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
        public static void Run(string[] args)
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

            queue.Enqueue(config.InputPath);

            try
            {
                ParseAndDiscover(queue, parsedModules, config, env, analyzer);
            }
            catch (CompilerException ex)
            {
                CompilerLogger.LogFatal(ex);
                return;
            }

            try
            {
                GenerateCode(parsedModules, generatedCFiles, config, env, analyzer);
            }
            catch (CompilerException ex)
            {
                CompilerLogger.LogFatal(ex);
                return;
            }

            if (config.BuildNative)
            {
                LinkNativeBinary(generatedCFiles, config, env);
            }
        }

        /// <summary>
        /// Recursively parses source files and discovers module dependencies.
        /// </summary>
        private static void ParseAndDiscover(
            Queue<string> queue,
            Dictionary<string, ProgramNode> parsedModules,
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
                    CompilerLogger.LogError($"Source file not found: {path}");
                    continue;
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
        private static void LinkNativeBinary(List<string> cFiles, CompilerConfig config, CompilationEnvironment env)
        {
            if (config.IsDebugMode) CompilerLogger.LogStep("\nLinking binaries...");

            string exeName = Path.GetFileNameWithoutExtension(config.InputPath);
            if (exeName.Equals("main", StringComparison.OrdinalIgnoreCase))
                exeName = new DirectoryInfo(env.ProjectRoot).Name;

            string exePath = Path.Combine(env.BinDir, $"{exeName}.exe");
            string sources = string.Join(" ", cFiles.Select(f => $"\"{Path.GetFullPath(f)}\""));
            string args = $"{sources} -o \"{exePath}\" -std=c11 -I\"{env.ObjDir}\"";

            if (config.IsDebugMode) CompilerLogger.LogInfo("Invoking native toolchain: gcc");

            ProcessExecutor.Run("gcc", args);

            if (config.RunOnSuccess)
            {
                RunApplication(exePath);
            }
            else
            {
                CompilerLogger.LogSuccess($"Build complete: {exePath}");
            }
        }

        /// <summary>
        /// Starts the compiled application and monitors its exit status.
        /// </summary>
        private static void RunApplication(string exePath)
        {
#if DEBUG
            CompilerLogger.LogStep("Running Application...");
            Console.WriteLine("--------------------------------------------------");
#endif
            if (!File.Exists(exePath))
            {
                CompilerLogger.LogError($"Executable not found at: {exePath}");
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });

            process?.WaitForExit();
#if DEBUG
            Console.WriteLine("--------------------------------------------------");
            CompilerLogger.LogInfo($"App exited with code: {process?.ExitCode ?? -1}");
#endif
        }
    }
}