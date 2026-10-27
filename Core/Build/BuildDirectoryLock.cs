namespace Sage.Core.Build
{
    /// <summary>
    /// Exclusive lock on an object directory so concurrent Sage builds sharing it (for example
    /// several test files of one project) do not interleave cache reads, compiles and writes.
    /// </summary>
    internal sealed class BuildDirectoryLock : IDisposable
    {
        private const string FileName = ".sage-build.lock";
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly FileStream _stream;

        private BuildDirectoryLock(FileStream stream) => _stream = stream;

        public static BuildDirectoryLock Acquire(string directory)
        {
            string path = Path.Combine(directory, FileName);
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                try
                {
                    return new BuildDirectoryLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
