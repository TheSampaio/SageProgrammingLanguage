using System.Collections.Concurrent;
using Sage.Utilities;

namespace Sage.Core.Build
{
    /// <summary>
    /// Identifies a native compiler driver by hashing its <c>-v</c> report (version, target triple,
    /// configuration, thread model, installation paths). Probed at most once per compiler process.
    /// </summary>
    internal static class NativeToolchainIdentity
    {
        private static readonly ConcurrentDictionary<string, Lazy<string>> Identities = new(StringComparer.Ordinal);

        /// <summary>Returns the identity hash of <paramref name="compiler"/>, probing it on first use.</summary>
        public static string Of(string compiler) => Identities.GetOrAdd(compiler, Probe).Value;

        /// <summary>
        /// Starts probing <paramref name="compiler"/> in the background so the probe overlaps the
        /// front end. A failure (for example a missing compiler) surfaces from <see cref="Of"/>.
        /// </summary>
        public static void Prefetch(string compiler) => Task.Run(() =>
        {
            try { Of(compiler); }
            catch (Exception) { /* Rethrown by the cached Lazy when the build asks for the identity. */ }
        });

        private static Lazy<string> Probe(string compiler) => new(() =>
        {
            long start = CompilerInstrumentation.Start();
            string identity = ContentHash.Of(ProcessExecutor.Capture(compiler, "-v"));
            CompilerInstrumentation.Stop("toolchain_probe", start);
            return identity;
        });
    }
}
