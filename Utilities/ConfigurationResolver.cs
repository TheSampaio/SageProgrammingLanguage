namespace Sage.Utilities
{
    public static class ConfigurationResolver
    {
        public static CompilerConfig Resolve(string[] args)
        {
#if DEBUG
            // Dev Environment Sandbox Fallback
            if (args.Length == 0)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
                string sandboxPath = Path.Combine(projectRoot, "Sandbox", "src", "main.sg");

                if (!Directory.Exists(Path.GetDirectoryName(sandboxPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(sandboxPath)!);
                }

                // CRITICAL FIX: Only create the file if it doesn't exist.
                // Previously, this wiped your 'main.sg' changes every time you ran the compiler!
                if (!File.Exists(sandboxPath))
                {
                    File.WriteAllText(sandboxPath, "use console;\nfunc main() : none { console::print_line(\"Debug Mode Working!\"); }");
                }

                return new CompilerConfig(sandboxPath, true, true, true);
            }
#endif

            if (args.Length == 0) throw new ArgumentException("Usage: sage <command> OR sage <file>");

            bool verbose = args.Contains("--verbose");
            bool clean = args.Contains("--clean");
            string command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "";

            string autoPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "main.sg");

            if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                ValidateEntry(autoPath);
                return new CompilerConfig(autoPath, verbose, true, true, clean);
            }

            if (command.Equals("build", StringComparison.OrdinalIgnoreCase))
            {
                ValidateEntry(autoPath);
                return new CompilerConfig(autoPath, verbose, true, false, clean);
            }

            string explicitFile = Path.GetFullPath(command);
            ValidateEntry(explicitFile);

            return new CompilerConfig(explicitFile, verbose, true, true, clean);
        }

        private static void ValidateEntry(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Entry point not found at '{path}'. Are you inside a Sage project root?");
        }
    }
}