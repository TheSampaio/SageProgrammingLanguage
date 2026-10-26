using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Sage.Utilities
{
    /// <summary>
    /// Development-only phase timings, counters and memory statistics.
    /// <para>
    /// Every recording method is <see cref="ConditionalAttribute"/> on <c>SAGE_INSTRUMENTATION</c>,
    /// so ordinary Debug and Release builds compile the calls away and stay quiet. Build the
    /// compiler with <c>dotnet build Sage.csproj -c Instrumented</c> (or
    /// <c>-p:SageInstrumentation=true</c>) to print a report to standard error after each run.
    /// </para>
    /// </summary>
    public static class CompilerInstrumentation
    {
        /// <summary>Phases in report order. Ownership analysis runs inside the semantic phase.</summary>
        private static readonly string[] Phases = ["lex", "parse", "semantic", "codegen", "cache", "native_compile", "link", "run"];

        private static readonly ConcurrentDictionary<string, long> PhaseTicks = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> Values = new(StringComparer.Ordinal);

        /// <summary>Returns a timestamp to pass to <see cref="Stop"/>. Costs one counter read when disabled.</summary>
        public static long Start() => Stopwatch.GetTimestamp();

        /// <summary>Adds the time elapsed since <paramref name="start"/> to <paramref name="phase"/>.</summary>
        [Conditional("SAGE_INSTRUMENTATION")]
        public static void Stop(string phase, long start)
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            PhaseTicks.AddOrUpdate(phase, elapsed, (_, total) => total + elapsed);
        }

        /// <summary>Records a named value, such as a cache hit count.</summary>
        [Conditional("SAGE_INSTRUMENTATION")]
        public static void Record(string name, object value) =>
            Values[name] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        /// <summary>Writes the report to standard error when anything was recorded.</summary>
        [Conditional("SAGE_INSTRUMENTATION")]
        public static void Report()
        {
            if (PhaseTicks.IsEmpty && Values.IsEmpty) return;

            var error = Console.Error;
            void Line(string name, object value) =>
                error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[sage-instrumentation] {name}={value}"));

            Line("sage_version", SageInfo.Version);
            foreach (var phase in Phases.Concat(PhaseTicks.Keys.Except(Phases).Order()))
            {
                if (PhaseTicks.TryGetValue(phase, out long ticks)) Line($"{phase}_ms", ToMilliseconds(ticks).ToString("F2", CultureInfo.InvariantCulture));
            }
            foreach (var (name, value) in Values.OrderBy(pair => pair.Key, StringComparer.Ordinal)) Line(name, value);

            using var process = Process.GetCurrentProcess();
            // Wall time since the process started, including .NET runtime startup.
            Line("process_ms", (DateTime.Now - process.StartTime).TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            Line("peak_working_set_bytes", process.PeakWorkingSet64);
            Line("allocated_bytes", GC.GetTotalAllocatedBytes());
            Line("gc_collections", $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        }

        private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
