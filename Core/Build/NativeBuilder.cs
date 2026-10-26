using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text;
using Sage.Utilities;

namespace Sage.Core.Build
{
    /// <summary>
    /// Compiles native translation units separately (<c>-c</c>) into object files and links them,
    /// reusing objects and executables whose inputs are unchanged.
    /// <para>
    /// An object is reused only when its cache key (compiler version, toolchain identity, compiler
    /// command and flags, source path, and the content of the source plus every header it includes
    /// transitively) matches the manifest and the object on disk still has the recorded hash. The
    /// executable is relinked whenever an object, the library set, the linker or the output changed.
    /// </para>
    /// Units compile in parallel; failures are reported in unit order so diagnostics stay deterministic.
    /// </summary>
    /// <param name="objectDirectory">Directory holding the objects, the manifest and the build lock.</param>
    /// <param name="forceClean">When true, discards all cached objects and the manifest first.</param>
    /// <param name="verbose">When true, logs a one-line compile/link summary.</param>
    public sealed class NativeBuilder(string objectDirectory, bool forceClean, bool verbose)
    {
        private const string KeySchema = "sage-native-cache/1";

        /// <summary>Builds <paramref name="units"/> and links them as described by <paramref name="link"/>.</summary>
        public NativeBuildResult Build(IReadOnlyList<NativeTranslationUnit> units, NativeLinkRequest link)
        {
            using var buildLock = BuildDirectoryLock.Acquire(objectDirectory);
            string manifestPath = Path.Combine(objectDirectory, BuildCacheManifest.FileName);

            if (forceClean) Clean(manifestPath, units);

            var manifest = BuildCacheManifest.Load(manifestPath, out bool recovered);
            if (recovered && verbose) CompilerLogger.LogWarning("Build cache manifest was unreadable; rebuilding all native units.");

            try
            {
                long cacheStart = CompilerInstrumentation.Start();
                var hasher = new IncludeClosureHasher();
                var keys = units.Select(unit => ObjectKey(unit, hasher)).ToList();
                var stale = Enumerable.Range(0, units.Count)
                    .Where(i => !IsCurrent(manifest.Objects.GetValueOrDefault(units[i].Name), keys[i], units[i].ObjectPath))
                    .ToList();
                CompilerInstrumentation.Stop("cache", cacheStart);

                // Forget stale entries before compiling, so a failed or interrupted compile never looks valid.
                foreach (int i in stale) manifest.Objects.Remove(units[i].Name);
                long compileStart = CompilerInstrumentation.Start();
                CompileUnits(units, stale, i =>
                    manifest.Objects[units[i].Name] = new CacheEntry(keys[i], ContentHash.OfFile(units[i].ObjectPath)));
                CompilerInstrumentation.Stop("native_compile", compileStart);

                long linkStart = CompilerInstrumentation.Start();
                bool linked = LinkIfNeeded(units, link, manifest);
                CompilerInstrumentation.Stop("link", linkStart);

                var result = new NativeBuildResult(
                    stale.Select(i => units[i].Name).ToList(),
                    Enumerable.Range(0, units.Count).Except(stale).Select(i => units[i].Name).ToList(),
                    linked);
                if (verbose) CompilerLogger.LogInfo(result.Describe());
                CompilerInstrumentation.Record("units_compiled", result.Compiled.Count);
                CompilerInstrumentation.Record("units_reused", result.Reused.Count);
                CompilerInstrumentation.Record("relinked", result.Linked);
                return result;
            }
            finally
            {
                manifest.Save(manifestPath);
            }
        }

        private static string ObjectKey(NativeTranslationUnit unit, IncludeClosureHasher hasher)
        {
            var key = new StringBuilder()
                .AppendLine(KeySchema)
                .AppendLine($"sage {SageInfo.Version}")
                .AppendLine($"compiler {unit.Compiler}")
                .AppendLine($"toolchain {NativeToolchainIdentity.Of(unit.Compiler)}")
                .AppendLine($"flags {string.Join(" ", unit.Flags)}")
                .AppendLine($"include-directories {unit.IncludeDirectories.Count}")
                .AppendLine($"source {unit.SourcePath}")
                .AppendLine($"closure {hasher.Fingerprint(unit.SourcePath, unit.IncludeDirectories)}");
            return ContentHash.Of(key.ToString());
        }

        private static bool IsCurrent(CacheEntry? entry, string key, string outputPath) =>
            entry != null && entry.Key == key && File.Exists(outputPath) && ContentHash.OfFile(outputPath) == entry.OutputHash;

        private static bool LinkIfNeeded(IReadOnlyList<NativeTranslationUnit> units, NativeLinkRequest link, BuildCacheManifest manifest)
        {
            string arguments = link.LinkArguments(units.Select(unit => unit.ObjectPath));
            var key = new StringBuilder()
                .AppendLine(KeySchema)
                .AppendLine($"linker {link.Linker}")
                .AppendLine($"toolchain {NativeToolchainIdentity.Of(link.Linker)}")
                .AppendLine($"arguments {arguments}");
            foreach (var unit in units) key.AppendLine($"object {manifest.Objects[unit.Name].OutputHash}");
            string linkKey = ContentHash.Of(key.ToString());

            if (IsCurrent(manifest.Links.GetValueOrDefault(link.OutputPath), linkKey, link.OutputPath)) return false;

            manifest.Links.Remove(link.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(link.OutputPath)!);
            ProcessExecutor.Run(link.Linker, arguments);
            manifest.Links[link.OutputPath] = new CacheEntry(linkKey, ContentHash.OfFile(link.OutputPath));
            return true;
        }

        private void Clean(string manifestPath, IReadOnlyList<NativeTranslationUnit> units)
        {
            File.Delete(manifestPath);
            var objects = Directory.EnumerateFiles(objectDirectory, "*.o").Concat(units.Select(unit => unit.ObjectPath));
            foreach (var path in objects.Distinct(StringComparer.OrdinalIgnoreCase).ToList()) File.Delete(path);
        }

        /// <summary>
        /// Compiles <paramref name="indices"/> of <paramref name="units"/> in parallel. Successful units are
        /// reported to <paramref name="onCompiled"/> in unit order before any failure is rethrown.
        /// </summary>
        private static void CompileUnits(IReadOnlyList<NativeTranslationUnit> units, IReadOnlyList<int> indices, Action<int> onCompiled)
        {
            var failures = new ConcurrentDictionary<int, Exception>();
            var succeeded = new ConcurrentBag<int>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.ForEach(indices, options, index =>
            {
                try
                {
                    ProcessExecutor.Run(units[index].Compiler, units[index].CompileArguments);
                    succeeded.Add(index);
                }
                catch (Exception ex)
                {
                    failures[index] = ex;
                }
            });

            foreach (int index in succeeded.Order()) onCompiled(index);
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
