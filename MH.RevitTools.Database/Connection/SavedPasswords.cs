using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MH.RevitTools.Database.Checking;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// The database passwords the person entered, for connection files that don't hold one. Each is kept encrypted for
    /// that person on this PC (<c>%LOCALAPPDATA%\MH\RevitTools\Passwords</c>), one file per server, database and login,
    /// and only that connection ever gets it: a connection file changed to name another server doesn't. Safe to use from
    /// several threads.
    /// </summary>
    public sealed class SavedPasswords
    {
        // An encrypted password is a few hundred characters.
        private const long MaxFileBytes = 64 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        private readonly object _gate = new object();
        private readonly string _folder;
        private readonly IPasswordProtection _protection;

        // By connection, for this session: the one in use, and the one the server refused.
        private readonly Dictionary<string, string> _known = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _refused = new Dictionary<string, string>(StringComparer.Ordinal);

        public SavedPasswords(string folder, IPasswordProtection protection)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A folder is required.", nameof(folder));
            _folder = folder;
            _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        }

        /// <summary><c>%LOCALAPPDATA%\MH\RevitTools\Passwords</c>.</summary>
        public static string DefaultFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH", "RevitTools", "Passwords");

        /// <summary>
        /// The connection file with the password in it, where it needs one that's known. Otherwise as it is, and saying
        /// so if the server refused the one that was kept.
        /// </summary>
        public ConnectionFileResult Complete(ConnectionFileResult file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (!file.IsLoaded || !file.Settings.NeedsPassword)
                return file;

            string connection = NameOf(file.Settings);
            lock (_gate)
            {
                string password = Find(connection);
                if (password != null)
                    return file.WithPassword(password);
                return _refused.ContainsKey(connection) ? file.WithRefusedPassword() : file;
            }
        }

        /// <summary>
        /// Keeps an entered password that works. When it can't be kept (false, with the reason), it's still used until
        /// Revit closes.
        /// </summary>
        public bool TrySave(ConnectionSettings settings, out string problem)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.HasEnteredPassword)
                throw new ArgumentException("Only an entered password is kept.", nameof(settings));

            string connection = NameOf(settings);
            lock (_gate)
            {
                _known[connection] = settings.Password;
                _refused.Remove(connection);
                return TryWrite(connection, settings.Password, out problem);
            }
        }

        /// <summary>
        /// The server refused this entered password: it isn't used again until Revit restarts, unless another one is
        /// entered. The kept file stays, as the refusal may have been the server's fault.
        /// </summary>
        public void Refuse(ConnectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.HasEnteredPassword)
                return;

            string connection = NameOf(settings), known;
            lock (_gate)
            {
                // A newer one has been entered meanwhile: this answer is about the older one.
                if (_known.TryGetValue(connection, out known) && !string.Equals(known, settings.Password, StringComparison.Ordinal))
                    return;
                _known.Remove(connection);
                _refused[connection] = settings.Password;
            }
        }

        private string Find(string connection)
        {
            string password, refused;
            if (_known.TryGetValue(connection, out password))
                return password;

            password = Read(connection);
            if (password == null
                || (_refused.TryGetValue(connection, out refused) && string.Equals(refused, password, StringComparison.Ordinal)))
                return null;

            _refused.Remove(connection);
            _known[connection] = password;
            return password;
        }

        private string Read(string connection)
        {
            try
            {
                string path = PathFor(connection);
                if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes)
                    return null;
                byte[] secret = _protection.Unprotect(Convert.FromBase64String(File.ReadAllText(path)), PurposeOf(connection));
                string password = secret == null ? null : Utf8.GetString(secret);
                return string.IsNullOrEmpty(password) ? null : password;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException
                || ex is DecoderFallbackException)
            {
                // Can't be read, or isn't what was written: the same as none, so the person is asked.
                return null;
            }
        }

        private bool TryWrite(string connection, string password, out string problem)
        {
            problem = null;
            try
            {
                byte[] data = _protection.Protect(Utf8.GetBytes(password), PurposeOf(connection));
                Directory.CreateDirectory(_folder);
                SafeFile.WriteAllText(PathFor(connection), Convert.ToBase64String(data));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException
                || ex is EncoderFallbackException)
            {
                problem = ex.Message;
                return false;
            }
        }

        private string PathFor(string connection)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Utf8.GetBytes(connection));
                var name = new StringBuilder();
                for (int i = 0; i < 8; i++)
                    name.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return Path.Combine(_folder, name + ".txt");
            }
        }

        private static byte[] PurposeOf(string connection) => Utf8.GetBytes("MH.RevitTools database password|" + connection);

        // However it's written: the server compares none of these by upper and lower case.
        private static string NameOf(ConnectionSettings settings) =>
            Upper(settings.Server) + "|" + Upper(settings.Database) + "|" + Upper(settings.Login);

        // A to Z only. Full upper-casing also turns some other letters into these, and then a name that only looks like the
        // server's would be the server's.
        private static string Upper(string text)
        {
            char[] letters = text.ToCharArray();
            for (int i = 0; i < letters.Length; i++)
            {
                if (letters[i] >= 'a' && letters[i] <= 'z')
                    letters[i] = (char)(letters[i] - 'a' + 'A');
            }
            return new string(letters);
        }
    }
}
