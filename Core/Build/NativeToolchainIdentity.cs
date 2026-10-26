using System.Collections.Concurrent;

namespace Sage.Core.Build
{
    /// <summary>
    /// Identifies a native compiler driver by hashing its <c>-v</c> report (version, target triple,
    /// configuration, thread model, installation paths). Probed at most once per compiler process.
    /// </summary>
    internal static class NativeToolchainIdentity
    {
        private static readonly ConcurrentDictionary<string, Lazy<string>> Identities = new(StringComparer.Ordinal);

        public static string Of(string compiler) =>
            Identities.GetOrAdd(compiler, command => new Lazy<string>(() => ContentHash.Of(ProcessExecutor.Capture(command, "-v")))).Value;
    }
}
