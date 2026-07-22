using System.Diagnostics;
using Sage.Core;
using Sage.Ast;
using Sage.Enums;

namespace Sage.Utilities
{
    /// <summary>
    /// Provides standardized, color-coded console logging for the Sage compiler pipeline.
    /// Handles Conditional compilation for Debug/Release output modes.
    /// </summary>
    public static class CompilerLogger
    {
        /// <summary>
        /// The source file currently being processed, used to produce accurate diagnostics
        /// (e.g. 'console.sg(12,5): error ...'). Set by the pipeline as it moves between modules.
        /// </summary>
        public static string? CurrentFile { get; set; }

        /// <summary>
        /// Logs a major milestone in the compilation process to the console.
        /// </summary>
        public static void LogStep(string message) => Console.WriteLine(message);

        /// <summary>
        /// Logs general information regarding the current compilation context.
        /// Prefixed with [INFO] in Debug mode.
        /// </summary>
        public static void LogInfo(string message)
        {
#if DEBUG
            Console.WriteLine($"[INFO] {message}");
#else
            Console.WriteLine(message);
#endif
        }

        /// <summary>
        /// Logs a non-fatal warning in yellow.
        /// </summary>
        public static void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
#if DEBUG
            Console.WriteLine($"[WARNING] {message}");
#else
            Console.WriteLine(message);
#endif
            Console.ResetColor();
        }

        /// <summary>
        /// Logs a specific code error linked to a source token.
        /// Formatted as 'file(line,col): error CODE: Message' for IDE integration.
        /// </summary>
        public static void LogError(Token token, string code, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            string file = CurrentFile ?? "main.sg";
            Console.WriteLine($"{file}({token.Line},{token.Column}): error {code}: {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// Logs a generic system error (e.g., file not found).
        /// </summary>
        public static void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
#if DEBUG
            Console.WriteLine($"[SYS ERROR] {message}");
#else
            Console.WriteLine(message);
#endif
            Console.ResetColor();
        }

        /// <summary>
        /// Logs a successful operation or phase completion in green.
        /// </summary>
        public static void LogSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
#if DEBUG
            Console.WriteLine($"[SUCCESS] {message}");
#else
            Console.WriteLine(message);
#endif
            Console.ResetColor();
        }

        /// <summary>
        /// Handles and logs critical failures, printing stack traces only in Debug mode.
        /// </summary>
        public static void LogFatal(Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            if (ex is CompilerException sce)
            {
                var targetToken = sce.OffendingToken ?? new Token(TokenType.Unknown, "", sce.Line, sce.Column);
                LogError(targetToken, sce.ErrorCode, sce.Message);
            }
            else
            {
#if DEBUG
                Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(ex.StackTrace);
                }
#else
                Console.WriteLine($"\n{ex.Message}");
#endif
            }

            Console.ResetColor();
        }

        /// <summary>
        /// Logs internal debug information in dark gray. 
        /// Entirely stripped from Release builds via [Conditional].
        /// </summary>
        [Conditional("DEBUG")]
        public static void LogDebug(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    [DEBUG] {message}");
            Console.ResetColor();
        }
    }
}