using System;
using System.Security.Cryptography;
using System.Text;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// Hashes a password the way Studio's PasswordHashService does, so the database can compare it with the stored
    /// hash: PBKDF2 over the password as UTF-8, with HMAC-SHA512, 100,000 iterations, the account's 32-byte salt
    /// and a 64-byte hash, both in base64. If Studio ever changes any of it, this changes in the same release.
    /// </summary>
    internal static class PasswordHash
    {
        internal const int SaltBytes = 32;
        internal const int HashBytes = 64;
        private const int Iterations = 100000;

        // Throws on what isn't text, as Studio's hashing does, instead of hashing a replacement for it.
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        /// <param name="password">As typed: spaces count.</param>
        /// <param name="salt">The account's salt, in base64.</param>
        /// <exception cref="FormatException">The salt isn't 32 bytes in base64.</exception>
        /// <exception cref="EncoderFallbackException">The password isn't text.</exception>
        internal static string Compute(string password, string salt)
        {
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            byte[] saltBytes = Convert.FromBase64String(salt ?? "");
            if (saltBytes.Length != SaltBytes)
                throw new FormatException("The salt is " + saltBytes.Length + " bytes long, not " + SaltBytes + ".");

            byte[] passwordBytes = Utf8.GetBytes(password);
            try
            {
                return Convert.ToBase64String(Derive(passwordBytes, saltBytes));
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        private static byte[] Derive(byte[] password, byte[] salt)
        {
#if NET6_0_OR_GREATER
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashBytes);
#else
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA512))
                return pbkdf2.GetBytes(HashBytes);
#endif
        }
    }
}
