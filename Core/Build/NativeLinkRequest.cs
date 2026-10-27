namespace Sage.Core.Build
{
    /// <summary>
    /// Describes the final link step: which executable to produce and which native libraries
    /// (from <c>extern</c> blocks) to pass as <c>-l</c> flags. The linker driver defaults to
    /// <c>gcc</c>; a build that contains C++ units can link through <c>g++</c> instead.
    /// </summary>
    /// <param name="OutputPath">Absolute path of the executable.</param>
    /// <param name="Libraries">Library names without the <c>-l</c> prefix.</param>
    /// <param name="Linker">Linker driver command.</param>
    public sealed record NativeLinkRequest(string OutputPath, IReadOnlyCollection<string> Libraries, string Linker = "gcc")
    {
        /// <summary>Command-line arguments that link <paramref name="objectPaths"/> in order.</summary>
        public string LinkArguments(IEnumerable<string> objectPaths)
        {
            string objects = string.Join(" ", objectPaths.Select(path => $"\"{path}\""));
            string libraries = Libraries.Count > 0
                ? " " + string.Join(" ", Libraries.OrderBy(l => l, StringComparer.Ordinal).Select(l => $"-l{l}"))
                : "";
            return $"{objects} -o \"{OutputPath}\"{libraries}";
        }
    }
}
