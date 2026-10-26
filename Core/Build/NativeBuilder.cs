using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Sage.Utilities;

namespace Sage.Core.Build
{
    /// <summary>
    /// Compiles native translation units separately (<c>-c</c>) into object files, then links
    /// the objects into the executable. Units compile in parallel; failures are reported in unit
    /// order so diagnostics stay deterministic.
    /// </summary>
    /// <param name="verbose">When true, logs a one-line compile/link summary.</param>
    public sealed class NativeBuilder(bool verbose)
    {
        /// <summary>Builds <paramref name="units"/> and links them as described by <paramref name="link"/>.</summary>
        public NativeBuildResult Build(IReadOnlyList<NativeTranslationUnit> units, NativeLinkRequest link)
        {
            CompileUnits(units);

            Directory.CreateDirectory(Path.GetDirectoryName(link.OutputPath)!);
            ProcessExecutor.Run(link.Linker, link.LinkArguments(units.Select(unit => unit.ObjectPath)));

            var result = new NativeBuildResult(units.Select(unit => unit.Name).ToList(), [], Linked: true);
            if (verbose) CompilerLogger.LogInfo(result.Describe());
            return result;
        }

        private static void CompileUnits(IReadOnlyList<NativeTranslationUnit> units)
        {
            var failures = new ConcurrentDictionary<int, Exception>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.For(0, units.Count, options, index =>
            {
                var unit = units[index];
                try
                {
                    ProcessExecutor.Run(unit.Compiler, unit.CompileArguments);
                }
                catch (Exception ex)
                {
                    failures[index] = ex;
                }
            });

            if (failures.IsEmpty) return;

            var ordered = failures.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
            if (ordered.Count == 1 || ordered[0] is FileNotFoundException)
            {
                ExceptionDispatchInfo.Capture(ordered[0]).Throw();
            }
            throw new InvalidOperationException(string.Join(Environment.NewLine, ordered.Select(ex => ex.Message)));
        }
    }
}
