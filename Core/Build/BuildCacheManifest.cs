using System.Text.Json;

namespace Sage.Core.Build
{
    /// <summary>A cached build output: the key it was built for and the hash of the file on disk.</summary>
    /// <param name="Key">Cache key of the inputs that produced the output.</param>
    /// <param name="OutputHash">Content hash of the produced object or executable.</param>
    public sealed record CacheEntry(string Key, string OutputHash);

    /// <summary>
    /// Persistent record of cached objects and links, stored as <c>obj/sage-build-cache.json</c>.
    /// Saved atomically (write to a temporary file, then replace). A missing, unreadable or
    /// incompatible manifest is treated as empty, which forces a clean rebuild.
    /// </summary>
    public sealed class BuildCacheManifest
    {
        /// <summary>File name of the manifest inside the object directory.</summary>
        public const string FileName = "sage-build-cache.json";

        private const int CurrentSchema = 1;

        /// <summary>Manifest format version.</summary>
        public int Schema { get; set; } = CurrentSchema;

        /// <summary>Cached objects by translation-unit name.</summary>
        public Dictionary<string, CacheEntry> Objects { get; set; } = [];

        /// <summary>Cached links by executable path.</summary>
        public Dictionary<string, CacheEntry> Links { get; set; } = [];

        /// <summary>Loads the manifest; <paramref name="recovered"/> is true when an existing file had to be discarded.</summary>
        public static BuildCacheManifest Load(string path, out bool recovered)
        {
            recovered = false;
            if (!File.Exists(path)) return new BuildCacheManifest();

            try
            {
                var manifest = JsonSerializer.Deserialize<BuildCacheManifest>(File.ReadAllText(path));
                if (manifest is { Schema: CurrentSchema, Objects: not null, Links: not null } &&
                    manifest.Objects.Values.Concat(manifest.Links.Values).All(entry => entry is { Key: not null, OutputHash: not null }))
                {
                    return manifest;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // Fall through: an unreadable manifest only costs a clean rebuild.
            }

            recovered = true;
            return new BuildCacheManifest();
        }

        /// <summary>Writes the manifest atomically.</summary>
        public void Save(string path)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
    }
}
