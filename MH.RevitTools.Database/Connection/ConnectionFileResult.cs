namespace MH.RevitTools.Database.Connection
{
    /// <summary>The outcome of reading the connection file: the settings, or what's wrong in plain words.</summary>
    public sealed class ConnectionFileResult
    {
        private ConnectionFileResult(string path, ConnectionSettings settings, string problem)
        {
            Path = path;
            Settings = settings;
            Problem = problem;
        }

        /// <summary>The file that was read.</summary>
        public string Path { get; }

        /// <summary>The settings; null when <see cref="Problem"/> says why not.</summary>
        public ConnectionSettings Settings { get; }

        /// <summary>What's wrong, in plain words and naming the file; null when the file was read.</summary>
        public string Problem { get; }

        public bool IsLoaded => Settings != null;

        internal static ConnectionFileResult Loaded(string path, ConnectionSettings settings) =>
            new ConnectionFileResult(path, settings, null);

        internal static ConnectionFileResult Failed(string path, string problem) =>
            new ConnectionFileResult(path, null, problem);
    }
}
