namespace Sage.Utilities
{
    public class CompilerConfig
    {
        public string InputPath { get; }
        public bool IsDebugMode { get; }
        public bool BuildNative { get; }
        public bool RunOnSuccess { get; }

        /// <summary>When true (<c>--clean</c>), cached native objects are discarded before building.</summary>
        public bool ForceClean { get; }

        public CompilerConfig(string inputPath, bool isDebugMode, bool buildNative, bool runOnSuccess, bool forceClean = false)
        {
            InputPath = inputPath;
            IsDebugMode = isDebugMode;
            BuildNative = buildNative;
            RunOnSuccess = runOnSuccess;
            ForceClean = forceClean;
        }
    }
}