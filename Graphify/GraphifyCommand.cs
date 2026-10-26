using System.Text.Json;

namespace Sage.Graphify
{
    /// <summary>
    /// The <c>sage graphify</c> command line. It runs the compiler front end only and never touches normal build or
    /// run behavior.
    /// </summary>
    public static class GraphifyCommand
    {
        private const string Usage = """
            Usage:
              sage graphify build [path] [--stats]
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
                _ => Fail($"Unknown graphify command '{args[0]}'.")
            };
        }

        private static int RunBuild(string[] args)
        {
            bool stats = args.Contains("--stats");
            var paths = args.Where(arg => arg != "--stats").ToList();
            if (paths.Any(path => path.StartsWith("--", StringComparison.Ordinal))) return Fail($"Unknown option '{paths.First(p => p.StartsWith("--"))}'.");
            if (paths.Count > 1) return Fail("'build' takes at most one path.");

            var result = GraphifyStore.Build(GraphifyProject.ResolveEntry(paths.FirstOrDefault()));
            if (stats)
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    modules = result.Index.Shards.Count,
                    written = result.Written,
                    unchanged = result.Unchanged,
                    removed = result.Removed
                }, GraphifyJson.Options));
            return 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine($"graphify: {message}");
            Console.Error.WriteLine(Usage);
            return 1;
        }
    }
}
