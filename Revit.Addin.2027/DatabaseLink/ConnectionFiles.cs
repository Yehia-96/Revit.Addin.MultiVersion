using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>The connection file this PC's Revit 2027 uses, and the database password that goes with it.</summary>
    internal static class ConnectionFiles
    {
        /// <summary>The database passwords entered on this PC, as the file built into the add-in holds none.</summary>
        public static SavedPasswords Passwords { get; } = new SavedPasswords(SavedPasswords.DefaultFolder, new WindowsPasswordProtection());

        /// <summary>
        /// The one built into this add-in, unless the PC has its own, as a developer's has. With the password in it, once
        /// that was entered. A broken file doesn't throw: the result says what's wrong.
        /// </summary>
        public static ConnectionFileResult Read() =>
            Passwords.Complete(ConnectionFile.ReadFirst(ConnectionFile.DefaultPath, typeof(ConnectionFiles).Assembly));

        /// <summary>The database of these settings. A password its server refuses isn't used again.</summary>
        public static ILinkDatabase Open(ConnectionSettings settings) =>
            new PasswordWatch(new SqlLinkDatabase(settings), () => Passwords.Refuse(settings));
    }
}
