namespace MH.RevitTools.Database.Connection
{
    /// <summary>Encrypts what only the person using Windows now, on this PC, may read again.</summary>
    public interface IPasswordProtection
    {
        /// <param name="secret">What to encrypt.</param>
        /// <param name="purpose">What it's for. Only the same purpose gets it back.</param>
        /// <exception cref="System.Security.Cryptography.CryptographicException">Windows can't encrypt now.</exception>
        byte[] Protect(byte[] secret, byte[] purpose);

        /// <summary>
        /// What was encrypted, or null: it's someone else's, from another PC, for another purpose, or damaged.
        /// </summary>
        byte[] Unprotect(byte[] data, byte[] purpose);
    }
}
