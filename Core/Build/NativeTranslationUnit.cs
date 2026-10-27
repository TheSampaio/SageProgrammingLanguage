namespace Sage.Core.Build
{
    /// <summary>
    /// One native source file compiled independently into an object file.
    /// The unit is language-neutral: generated C modules use <see cref="ForC"/>, while other
    /// native sources (for example C++ adapters compiled with <c>g++ -std=c++17</c>) can be
    /// described with the primary constructor and go through the same compile/cache path.
    /// </summary>
    /// <param name="Name">Stable unit name, unique within one object directory (e.g. the module name).</param>
    /// <param name="SourcePath">Absolute path of the source file.</param>
    /// <param name="ObjectPath">Absolute path of the object file to produce.</param>
    /// <param name="Compiler">Compiler driver command (e.g. <c>gcc</c>, <c>g++</c>).</param>
    /// <param name="Flags">Language/code-generation flags, excluding include directories.</param>
    /// <param name="IncludeDirectories">Include search directories, in <c>-I</c> order.</param>
    public sealed record NativeTranslationUnit(
        string Name,
        string SourcePath,
        string ObjectPath,
        string Compiler,
        IReadOnlyList<string> Flags,
        IReadOnlyList<string> IncludeDirectories)
    {
        /// <summary>Compiler driver for generated C modules.</summary>
        public const string CCompiler = "gcc";

        /// <summary>Flags used for every generated C module (the language standard Sage targets).</summary>
        public static readonly IReadOnlyList<string> CFlags = ["-std=c11"];

        /// <summary>Compiler driver for C++ sources and generated C++ adapters.</summary>
        public const string CppCompiler = "g++";

        /// <summary>Flags used for every C++ unit.</summary>
        public static readonly IReadOnlyList<string> CppFlags = ["-std=c++17"];

        /// <summary>Describes a C++ source compiled into <paramref name="objectPath"/>.</summary>
        public static NativeTranslationUnit ForCpp(string sourcePath, string objectPath, IReadOnlyList<string> includeDirectories) =>
            new(Path.GetFileNameWithoutExtension(objectPath), sourcePath, objectPath, CppCompiler, CppFlags, includeDirectories);

        /// <summary>Describes a generated C module; its object file is placed next to the source.</summary>
        public static NativeTranslationUnit ForC(string sourcePath, IReadOnlyList<string> includeDirectories)
        {
            string fullPath = Path.GetFullPath(sourcePath);
            return new NativeTranslationUnit(
                Path.GetFileNameWithoutExtension(fullPath),
                fullPath,
                Path.ChangeExtension(fullPath, ".o"),
                CCompiler,
                CFlags,
                includeDirectories);
        }

        /// <summary>Command-line arguments that compile this unit (without linking).</summary>
        public string CompileArguments
        {
            get
            {
                string flags = string.Join(" ", Flags);
                string includes = string.Join(" ", IncludeDirectories.Select(dir => $"-I\"{dir}\""));
                return $"-c \"{SourcePath}\" -o \"{ObjectPath}\" {flags} {includes}".TrimEnd();
            }
        }
    }
}
