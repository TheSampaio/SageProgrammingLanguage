using Sage.Ast;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Project native sources declared with <c>source "file";</c> inside extern blocks, resolved
    /// relative to the declaring .sg file. C sources join the gcc build; C++ sources are compiled
    /// by g++ and pull in the C++ runtime. Their directories are added to the include path.
    /// </summary>
    public sealed class NativeSourceSet
    {
        private static readonly HashSet<string> CExtensions = new(StringComparer.OrdinalIgnoreCase) { ".c" };
        private static readonly HashSet<string> CppExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cpp", ".cc", ".cxx" };

        public IReadOnlyList<string> CSources { get; }
        public IReadOnlyList<string> CppSources { get; }
        public IReadOnlyList<string> IncludeDirectories { get; }

        private NativeSourceSet(List<string> cSources, List<string> cppSources)
        {
            CSources = cSources;
            CppSources = cppSources;
            IncludeDirectories = cSources.Concat(cppSources).Select(path => Path.GetDirectoryName(path)!)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Resolves and validates every declared source before code generation. A missing file or an
        /// unsupported extension is reported (S211) against the module that declared it.
        /// </summary>
        /// <param name="blocks">Extern blocks paired with the full path of their declaring .sg file.</param>
        public static NativeSourceSet Collect(IEnumerable<(ExternBlockNode Block, string ModulePath)> blocks)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cSources = new List<string>();
            var cppSources = new List<string>();
            foreach (var (block, modulePath) in blocks)
            {
                foreach (string declared in block.Sources)
                {
                    CompilerLogger.CurrentFile = Path.GetFileName(modulePath);
                    string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modulePath)!, declared));
                    string extension = Path.GetExtension(path);
                    bool isC = CExtensions.Contains(extension);
                    if (!isC && !CppExtensions.Contains(extension))
                        throw new CompilerException(block, "S211", $"Native source '{declared}' must be a .c, .cpp, .cc, or .cxx file.");
                    if (!File.Exists(path))
                        throw new CompilerException(block, "S211", $"Native source '{declared}' was not found at '{path}'.");
                    if (seen.Add(path)) (isC ? cSources : cppSources).Add(path);
                }
            }
            return new NativeSourceSet(cSources, cppSources);
        }
    }
}
