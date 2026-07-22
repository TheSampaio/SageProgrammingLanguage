using Sage.Core;
using Sage.Utilities;

namespace Sage
{
    /// <summary>
    /// Entry point for the Sage Compiler CLI.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Primary execution loop for the compiler tool.
        /// </summary>
        private static void Main(string[] args)
        {
            ConsoleHelper.EnableAnsiSupport();

            try
            {
                if (HandleMetaCommands(args)) return;
                CompilationPipeline.Run(args);
            }
            catch (Exception ex)
            {
                CompilerLogger.LogFatal(ex);
            }
        }

        /// <summary>
        /// Processes high-level CLI commands such as versioning and project initialization.
        /// </summary>
        /// <returns>True if a meta-command was handled and the application should exit.</returns>
        private static bool HandleMetaCommands(string[] args)
        {
            if (args.Contains("--version") || args.Contains("-v"))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Sage Compiler v{SageInfo.FullVersion}");
                Console.ResetColor();
                return true;
            }

            if (args.Length >= 2 && args[0].Equals("new", StringComparison.OrdinalIgnoreCase))
            {
                ProjectInitializer.Initialize(args[1]);
                return true;
            }

            return false;
        }
    }
}