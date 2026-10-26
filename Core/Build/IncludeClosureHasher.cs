using System.Text;
using System.Text.RegularExpressions;

namespace Sage.Core.Build
{
    /// <summary>
    /// Fingerprints a native source together with every header it can include, transitively.
    /// Includes are resolved like GCC does: quoted includes search the including file's directory
    /// first, then the <c>-I</c> directories in order; angle includes search only the <c>-I</c>
    /// directories. Headers found there are hashed by content; anything else is a system header,
    /// recorded by name and covered by the toolchain identity. Every <c>#include</c> line is
    /// considered regardless of conditional compilation, which can only over-approximate.
    /// Locations are recorded relative to the include directory index (not as absolute paths),
    /// so identical headers installed in different places produce the same fingerprint.
    /// </summary>
    internal sealed partial class IncludeClosureHasher
    {
        private readonly Dictionary<string, ScannedFile> _scanned = new(StringComparer.OrdinalIgnoreCase);

        private sealed record ScannedFile(string Hash, IReadOnlyList<IncludeDirective> Includes);

        private sealed record IncludeDirective(bool Quoted, string Name);

        [GeneratedRegex(@"^[ \t]*#[ \t]*include[ \t]*([<""])([^>""\r\n]+)[>""]", RegexOptions.Multiline)]
        private static partial Regex IncludePattern();

        /// <summary>Returns a hash covering the source and all headers it reaches.</summary>
        public string Fingerprint(string sourcePath, IReadOnlyList<string> includeDirectories)
        {
            var record = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(Path.GetFullPath(sourcePath), "source", includeDirectories, visited, record);
            return ContentHash.Of(record.ToString());
        }

        private void Visit(string path, string label, IReadOnlyList<string> includeDirectories, HashSet<string> visited, StringBuilder record)
        {
            var file = Scan(path);
            record.Append(label).Append(' ').AppendLine(file.Hash);

            foreach (var include in file.Includes)
            {
                var (resolved, location) = Resolve(include, Path.GetDirectoryName(path)!, includeDirectories);
                if (resolved == null)
                {
                    record.Append("system ").AppendLine(include.Name);
                }
                else if (visited.Add(resolved))
                {
                    Visit(resolved, $"{location}:{include.Name}", includeDirectories, visited, record);
                }
            }
        }

        private static (string? Path, string Location) Resolve(IncludeDirective include, string currentDirectory, IReadOnlyList<string> includeDirectories)
        {
            if (include.Quoted)
            {
                string local = Path.GetFullPath(Path.Combine(currentDirectory, include.Name));
                if (File.Exists(local)) return (local, "local");
            }
            for (int i = 0; i < includeDirectories.Count; i++)
            {
                string candidate = Path.GetFullPath(Path.Combine(includeDirectories[i], include.Name));
                if (File.Exists(candidate)) return (candidate, $"I{i}");
            }
            return (null, "system");
        }

        private ScannedFile Scan(string path)
        {
            if (_scanned.TryGetValue(path, out var cached)) return cached;

            byte[] content = File.ReadAllBytes(path);
            var includes = IncludePattern().Matches(Encoding.UTF8.GetString(content))
                .Select(match => new IncludeDirective(match.Groups[1].Value == "\"", match.Groups[2].Value.Trim()))
                .ToList();
            var scanned = new ScannedFile(ContentHash.Of(content), includes);
            _scanned[path] = scanned;
            return scanned;
        }
    }
}
