using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sage.Utilities;

namespace Sage.Graphify
{
    /// <summary>The persisted index of a project: the manifest and every module shard, ordered by module name.</summary>
    public sealed record GraphifyIndex(IndexManifest Manifest, IReadOnlyList<ModuleShard> Shards);

    /// <summary>The outcome of an index build: which shards were rewritten, kept, or removed.</summary>
    public sealed record GraphifyBuildResult(GraphifyIndex Index, IReadOnlyList<string> Written, IReadOnlyList<string> Unchanged, IReadOnlyList<string> Removed);

    /// <summary>JSON settings shared by shards, the manifest, and query output.</summary>
    public static class GraphifyJson
    {
        /// <summary>Compact, camelCase, declaration-ordered output that omits default values.</summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Builds and persists the semantic index under <c>obj/graphify</c>: one JSON shard per module plus a manifest.
    /// A shard is keyed by the compiler version and source text and carries a hash of its dependencies, so a rebuild
    /// rewrites only shards whose own source, compiler, or dependencies changed.
    /// </summary>
    public static class GraphifyStore
    {
        /// <summary>Version of the persisted JSON layout.</summary>
        public const int SchemaVersion = 1;
        private const string ManifestFile = "manifest.json";
        private const string ShardDirectory = "modules";

        /// <summary>Analyzes the project and brings the persisted index up to date.</summary>
        /// <param name="entryPath">The entry source file; see <see cref="GraphifyProject.ResolveEntry"/>.</param>
        public static GraphifyBuildResult Build(string entryPath)
        {
            var project = GraphifyProject.Analyze(entryPath);
            var context = new GraphifyContext(project.Modules);
            var shards = new List<(ModuleShard Shard, IReadOnlySet<string> Referenced)>();
            foreach (var module in project.Modules.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var indexer = new ModuleIndexer(context);
                var shard = indexer.Build(module, RelativeFile(project, module));
                shards.Add((shard with { Key = Hash(SageInfo.Version + "\n" + module.Text) }, indexer.ReferencedModules));
            }

            var completed = AssignDependencyHashes(shards);
            string directory = Path.Combine(project.Environment.ObjDir, "graphify");
            string entry = RelativeToRoot(project.Environment.ProjectRoot, project.EntryPath);
            var manifest = new IndexManifest(SchemaVersion, SageInfo.Version, entry,
                completed.Select(s => new ManifestModule(s.Module, s.File, s.Origin, s.Key, s.Deps)).ToList());
            return Persist(directory, new GraphifyIndex(manifest, completed));
        }

        /// <summary>Loads the persisted index of a project, or null when none exists or it is unreadable.</summary>
        public static GraphifyIndex? Load(string entryPath)
        {
            string directory = Path.Combine(
                EnvironmentFactory.Create(new CompilerConfig(entryPath, false, false, false)).ObjDir, "graphify");
            string manifestPath = Path.Combine(directory, ManifestFile);
            if (!File.Exists(manifestPath)) return null;
            try
            {
                var manifest = JsonSerializer.Deserialize<IndexManifest>(File.ReadAllText(manifestPath), GraphifyJson.Options);
                if (manifest is not { Schema: SchemaVersion }) return null;
                var shards = new List<ModuleShard>();
                foreach (var row in manifest.Modules)
                {
                    var shard = JsonSerializer.Deserialize<ModuleShard>(
                        File.ReadAllText(Path.Combine(directory, ShardDirectory, row.Module + ".json")), GraphifyJson.Options);
                    if (shard == null) return null;
                    shards.Add(shard);
                }
                return new GraphifyIndex(manifest, shards);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }

        private static GraphifyBuildResult Persist(string directory, GraphifyIndex index)
        {
            string shardDirectory = Path.Combine(directory, ShardDirectory);
            Directory.CreateDirectory(shardDirectory);
            var written = new List<string>();
            var unchanged = new List<string>();
            foreach (var shard in index.Shards)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(shard, GraphifyJson.Options));
                (WriteIfChanged(Path.Combine(shardDirectory, shard.Module + ".json"), bytes) ? written : unchanged).Add(shard.Module);
            }
            WriteIfChanged(Path.Combine(directory, ManifestFile),
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(index.Manifest, GraphifyJson.Options)));

            var current = index.Shards.Select(s => s.Module).ToHashSet(StringComparer.Ordinal);
            var removed = new List<string>();
            foreach (string file in Directory.GetFiles(shardDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string module = Path.GetFileNameWithoutExtension(file);
                if (current.Contains(module)) continue;
                File.Delete(file);
                removed.Add(module);
            }
            return new GraphifyBuildResult(index, written, unchanged, removed);
        }

        /// <summary>Writes the file atomically unless it already holds exactly these bytes; returns whether it was written.</summary>
        private static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return false;
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            return true;
        }

        /// <summary>
        /// Hashes each module's dependency closure: modules reachable through <c>use</c> plus modules whose declarations
        /// the shard references directly. Changing any of them changes the shard's <c>deps</c> value.
        /// </summary>
        private static List<ModuleShard> AssignDependencyHashes(List<(ModuleShard Shard, IReadOnlySet<string> Referenced)> shards)
        {
            var byName = shards.ToDictionary(s => s.Shard.Module, s => s, StringComparer.Ordinal);
            var result = new List<ModuleShard>(shards.Count);
            foreach (var (shard, referenced) in shards)
            {
                var dependencies = new SortedSet<string>(StringComparer.Ordinal);
                var pending = new Stack<string>((shard.Uses ?? []).Select(u => u.Module).Concat(referenced));
                while (pending.Count > 0)
                {
                    string name = pending.Pop();
                    if (name == shard.Module || !byName.TryGetValue(name, out var dependency) || !dependencies.Add(name)) continue;
                    foreach (var use in dependency.Shard.Uses ?? []) pending.Push(use.Module);
                }
                string material = string.Join("\n", dependencies.Select(name => $"{name}:{byName[name].Shard.Key}"));
                result.Add(shard with { Deps = Hash(material) });
            }
            return result;
        }

        private static string RelativeFile(AnalyzedProject project, LoadedModule module) =>
            module.IsStd ? "std/" + Path.GetFileName(module.Path) : RelativeToRoot(project.Environment.ProjectRoot, module.Path);

        private static string RelativeToRoot(string root, string path) =>
            Path.GetRelativePath(root, path).Replace('\\', '/');

        /// <summary>Short, stable content hash (first 16 hex digits of SHA-256).</summary>
        internal static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}
