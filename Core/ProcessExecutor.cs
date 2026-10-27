using System.Diagnostics;
using System.Text;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Provides a synchronized wrapper for executing external system processes.
    /// Handles standard output/error redirection and provides detailed error reporting
    /// if a process fails or a tool is missing from the system PATH.
    /// </summary>
    internal static class ProcessExecutor
    {
        /// <summary>
        /// Executes an external command-line tool, waits for its completion, and captures its output.
        /// </summary>
        /// <param name="fileName">The name or absolute path of the executable tool (e.g., "gcc").</param>
        /// <param name="arguments">The command-line arguments to pass to the tool.</param>
        /// <exception cref="FileNotFoundException">Thrown if the specified tool is not found in the system PATH.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the process returns a non-zero exit code, indicating failure.</exception>
        public static void Run(string fileName, string arguments)
        {
            string output = Execute(fileName, arguments).Output;

            // Log the tool's output for debugging if the compiler is running in Verbose/Debug mode
            if (output.Length > 0)
            {
                CompilerLogger.LogDebug($"[{fileName}] {output}");
            }
        }

        /// <summary>
        /// Executes an external tool and returns everything it wrote to standard output and standard error.
        /// Used for toolchain probes such as <c>gcc -v</c>, which reports on standard error.
        /// </summary>
        /// <exception cref="FileNotFoundException">Thrown if the tool is not found in the system PATH.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the process returns a non-zero exit code.</exception>
        public static string Capture(string fileName, string arguments)
        {
            var (output, error) = Execute(fileName, arguments);
            return output + error;
        }

        private static (string Output, string Error) Execute(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            // Asynchronously capture the tool's output and error streams
            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            try
            {
                process.Start();

                // Begin reading the streams to prevent the process from hanging on buffer limits
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Block execution until the external tool (like GCC) finishes its task
                process.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Specifically catch cases where the OS cannot find the file in the environment PATH
                throw new FileNotFoundException($"Tool not found or not in PATH: {fileName}");
            }

            // If the tool returns an exit code other than 0, it means compilation or linking failed
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Process failed ({fileName}):\n{error}\n{output}");
            }

            return (output.ToString(), error.ToString());
        }
    }
}