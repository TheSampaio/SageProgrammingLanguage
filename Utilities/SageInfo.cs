namespace Sage.Utilities
{
    /// <summary>
    /// Single source of truth for compiler metadata. Bump <see cref="Version"/> on every change
    /// to the compiler so the CLI, docs, and generated artifacts stay in sync.
    /// </summary>
    public static class SageInfo
    {
        /// <summary>Semantic version of the Sage compiler.</summary>
        public const string Version = "0.8.0";

        /// <summary>Release channel label shown alongside the version.</summary>
        public const string Channel = "Alpha";

        /// <summary>Human-readable version string, e.g. "0.7.0 (Alpha)".</summary>
        public static string FullVersion => $"{Version} ({Channel})";
    }
}
