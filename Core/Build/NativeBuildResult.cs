namespace Sage.Core.Build
{
    /// <summary>Outcome of a native build: which units were compiled or reused, and whether the link ran.</summary>
    /// <param name="Compiled">Names of the units compiled during this build, in unit order.</param>
    /// <param name="Reused">Names of the units whose cached objects were reused, in unit order.</param>
    /// <param name="Linked">True when the executable was (re)linked.</param>
    public sealed record NativeBuildResult(IReadOnlyList<string> Compiled, IReadOnlyList<string> Reused, bool Linked)
    {
        /// <summary>Compact one-line summary used by <c>--verbose</c>.</summary>
        public string Describe() =>
            $"Native compile: {Compiled.Count} compiled [{string.Join(", ", Compiled)}], " +
            $"{Reused.Count} reused [{string.Join(", ", Reused)}]; link: {(Linked ? "relinked" : "up to date")}";
    }
}
