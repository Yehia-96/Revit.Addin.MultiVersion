using System;
using System.Security.Cryptography;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// Windows' own encryption for the signed-in person (DPAPI). A copy of the data is of no use to another person or on
    /// another PC.
    /// </summary>
    public sealed class WindowsPasswordProtection : IPasswordProtection
    {
        // Revit runs on Windows only, and so does this library.
#pragma warning disable CA1416
        public byte[] Protect(byte[] secret, byte[] purpose)
        {
            if (secret == null)
                throw new ArgumentNullException(nameof(secret));
            return ProtectedData.Protect(secret, purpose, DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] data, byte[] purpose)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            try
            {
                return ProtectedData.Unprotect(data, purpose, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
#pragma warning restore CA1416
    }
}
