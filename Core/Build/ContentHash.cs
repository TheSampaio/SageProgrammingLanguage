using System.Security.Cryptography;
using System.Text;

namespace Sage.Core.Build
{
    /// <summary>SHA-256 content hashes used by the native build cache.</summary>
    internal static class ContentHash
    {
        public static string Of(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

        public static string Of(string text) => Of(Encoding.UTF8.GetBytes(text));

        public static string OfFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
