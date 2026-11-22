using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// Reads the connection file, <c>C:\ProgramData\MH\RevitTools\database.json</c>: one per PC, for
    /// every Revit year. The installer writes it and the add-in only reads it. It names the SQL
    /// Server and how to sign in: with the read-only login, or as the Windows user on a developer PC.
    /// It also names the database's master shared parameter file, so each PC pairs a database with
    /// its own file.
    /// </summary>
    public static class ConnectionFile
    {
        // A real one is a few hundred bytes. Anything far bigger isn't a connection file, and reading
        // it would only stall Revit.
        private const long MaxFileBytes = 64 * 1024;

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        };

        public static string DefaultPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MH", "RevitTools", "database.json");

        /// <summary>
        /// Reads and checks the file. A missing or broken file doesn't throw: the result says what's
        /// wrong. Problems never quote the file's values, since one of them may be the password.
        /// </summary>
        public static ConnectionFileResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A path is required.", nameof(path));

            string json;
            try
            {
                if (!File.Exists(path))
                    return ConnectionFileResult.Failed(path, "No connection file at " + path + ". The installer puts it there.");
                if (new FileInfo(path).Length > MaxFileBytes)
                    return ConnectionFileResult.Failed(path, "The connection file " + path + " is too big to be one (over 64 KB).");
                json = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                return ConnectionFileResult.Failed(path, "Can't read the connection file " + path + ": " + ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return ConnectionFileResult.Failed(path, "You don't have permission to read the connection file " + path + ".");
            }

            string problem;
            JObject root = ParseObject(json, out problem);
            ConnectionSettings settings = root == null ? null : ToSettings(root, out problem);
            string masterParameterFile = null;
            if (settings != null && !TryGetText(root, "masterParameterFile", out masterParameterFile, out problem))
                settings = null;
            return settings == null
                ? ConnectionFileResult.Failed(path, "The connection file " + path + " " + problem)
                : ConnectionFileResult.Loaded(path, settings, ExpandedPath(masterParameterFile));
        }

        // A path as written, with %LOCALAPPDATA% and the like filled in for the user running Revit.
        private static string ExpandedPath(string path) =>
            string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim());

        // The file's { } block, or null with the reason as the end of a sentence.
        private static JObject ParseObject(string json, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                problem = "is empty.";
                return null;
            }

            JToken token;
            try
            {
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 })
                {
                    token = JToken.ReadFrom(reader, LoadSettings);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            problem = "has something after the closing } (line " + reader.LineNumber + ").";
                            return null;
                        }
                    }
                }
            }
            catch (JsonReaderException ex)
            {
                // Only the position: the reader's own message can quote the text around the mistake,
                // and that may be the password.
                problem = "isn't valid JSON (line " + ex.LineNumber + ", position " + ex.LinePosition + ").";
                return null;
            }

            if (token.Type == JTokenType.Null)
            {
                problem = "is empty.";
                return null;
            }

            var root = token as JObject;
            if (root == null)
                problem = "should hold a single { } block with the settings.";
            return root;
        }

        // The settings, or null with the reason as the end of a sentence.
        private static ConnectionSettings ToSettings(JObject root, out string problem)
        {
            string server, database, login, password;
            bool? windowsSignIn, encrypt, trustServerCertificate;
            if (!TryGetText(root, "server", out server, out problem)
                || !TryGetText(root, "database", out database, out problem)
                || !TryGetText(root, "login", out login, out problem)
                || !TryGetText(root, "password", out password, out problem)
                || !TryGetFlag(root, "windowsSignIn", out windowsSignIn, out problem)
                || !TryGetFlag(root, "encrypt", out encrypt, out problem)
                || !TryGetFlag(root, "trustServerCertificate", out trustServerCertificate, out problem))
            {
                return null;
            }

            bool useWindowsSignIn = windowsSignIn ?? false;
            if (string.IsNullOrWhiteSpace(server))
                problem = "has no \"server\".";
            else if (string.IsNullOrWhiteSpace(database))
                problem = "has no \"database\".";
            else if (!useWindowsSignIn && string.IsNullOrWhiteSpace(login))
                problem = "has no \"login\". It needs the read-only login and its \"password\" "
                    + "(or \"windowsSignIn\": true on a developer PC).";
            else if (!useWindowsSignIn && string.IsNullOrEmpty(password))
                problem = "has no \"password\" for the login \"" + login.Trim() + "\".";

            if (problem != null)
                return null;

            // Encrypted, with the certificate checked, unless the file says otherwise.
            return new ConnectionSettings(server, database, useWindowsSignIn, login, password,
                encrypt ?? true, trustServerCertificate ?? false);
        }

        private static bool TryGetText(JObject root, string key, out string value, out string problem)
        {
            value = null;
            JToken token;
            if (!TryFind(root, key, out token, out problem))
                return false;
            if (token == null || token.Type == JTokenType.Null)
                return true;
            if (token.Type != JTokenType.String)
            {
                problem = "has \"" + key + "\" as something other than text in quotes" + LineOf(token) + ".";
                return false;
            }
            value = (string)token;
            return true;
        }

        private static bool TryGetFlag(JObject root, string key, out bool? value, out string problem)
        {
            value = null;
            JToken token;
            if (!TryFind(root, key, out token, out problem))
                return false;
            if (token == null || token.Type == JTokenType.Null)
                return true;
            if (token.Type != JTokenType.Boolean)
            {
                problem = "has \"" + key + "\" set to something other than true or false" + LineOf(token) + ".";
                return false;
            }
            value = (bool)token;
            return true;
        }

        // The key's value (null if absent), matched ignoring case, so "Server" works too. Two keys that
        // differ only in case are a problem rather than a silent pick.
        private static bool TryFind(JObject root, string key, out JToken value, out string problem)
        {
            value = null;
            problem = null;
            foreach (JProperty property in root.Properties())
            {
                if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (value != null)
                {
                    problem = "has \"" + key + "\" more than once" + LineOf(property) + ".";
                    return false;
                }
                value = property.Value;
            }
            return true;
        }

        private static string LineOf(JToken token)
        {
            var info = (IJsonLineInfo)token;
            return info.HasLineInfo() ? " (line " + info.LineNumber + ")" : "";
        }
    }
}
