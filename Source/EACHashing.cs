using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RosterRotation
{
    /// <summary>
    /// Reuses the SHA-256 provider for EAC save/archive content hashes. Hashing remains
    /// serialized so the helper is safe if a future integration ever invokes it off the
    /// normal KSP main thread.
    /// </summary>
    internal static class EACHashing
    {
        private static readonly object Sync = new object();
        private static readonly SHA256 Sha256 = SHA256.Create();

        internal static string ComputeSha256Hex(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] hash;
            lock (Sync)
            {
                hash = Sha256.ComputeHash(bytes);
            }

            var hex = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }
    }
}
