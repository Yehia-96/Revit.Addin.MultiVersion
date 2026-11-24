using System;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// The MH database couldn't be read. <see cref="Exception.Message"/> says why in plain words and
    /// never holds the password; the inner exception has the technical side.
    /// </summary>
    public sealed class LinkDatabaseException : Exception
    {
        public LinkDatabaseException(string message)
            : base(message)
        {
        }

        public LinkDatabaseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public LinkDatabaseException(string message, Exception innerException, bool isLoginRefused)
            : base(message, innerException)
        {
            IsLoginRefused = isLoginRefused;
        }

        /// <summary>The server answered, but doesn't take the connection's login with this password.</summary>
        public bool IsLoginRefused { get; }
    }
}
