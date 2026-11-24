namespace MH.RevitTools.Database.Connection
{
    /// <summary>The outcome of reading the connection file: the settings, or what's wrong in plain words.</summary>
    public sealed class ConnectionFileResult
    {
        private ConnectionFileResult(
            string source, ConnectionSettings settings, string masterParameterFile, string problem, bool isSavedPasswordRefused)
        {
            Source = source;
            Settings = settings;
            MasterParameterFile = masterParameterFile;
            Problem = problem;
            IsSavedPasswordRefused = isSavedPasswordRefused;
        }

        /// <summary>Which file was read: its path, or <see cref="ConnectionFile.BuiltIn"/>.</summary>
        public string Source { get; }

        /// <summary>
        /// The settings; null when <see cref="Problem"/> says why not. They may still need the password
        /// (<see cref="ConnectionSettings.NeedsPassword"/>).
        /// </summary>
        public ConnectionSettings Settings { get; }

        /// <summary>The server refused the password kept on this PC, so <see cref="Settings"/> need a new one.</summary>
        public bool IsSavedPasswordRefused { get; }

        /// <summary>
        /// Where Studio writes the master shared parameter file for this database, with environment
        /// variables such as %LOCALAPPDATA% filled in; null when the file doesn't say.
        /// </summary>
        public string MasterParameterFile { get; }

        /// <summary>What's wrong, in plain words and saying which file; null when it was read.</summary>
        public string Problem { get; }

        public bool IsLoaded => Settings != null;

        internal static ConnectionFileResult Loaded(string source, ConnectionSettings settings, string masterParameterFile) =>
            new ConnectionFileResult(source, settings, masterParameterFile, null, false);

        internal static ConnectionFileResult Failed(string source, string problem) =>
            new ConnectionFileResult(source, null, null, problem, false);

        internal ConnectionFileResult WithPassword(string password) =>
            new ConnectionFileResult(Source, Settings.WithPassword(password), MasterParameterFile, null, false);

        internal ConnectionFileResult WithRefusedPassword() =>
            new ConnectionFileResult(Source, Settings, MasterParameterFile, null, true);
    }
}
