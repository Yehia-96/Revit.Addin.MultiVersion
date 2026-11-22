using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// Studio's master shared parameter file (MH_Parameter.txt), read: which parameters it holds, by
    /// GUID. Revit's own format: tab-separated, a <c>*PARAM</c> line naming the columns, then one
    /// <c>PARAM</c> line per parameter. Studio writes it as UTF-16 with a byte order mark; any encoding
    /// with a mark, and UTF-8 without one, read too. Immutable.
    /// </summary>
    public sealed class MasterParameterFile
    {
        // One line per parameter: a real one is some tens of kilobytes. Far bigger isn't one.
        private const long MaxFileBytes = 16L * 1024 * 1024;

        private static readonly IReadOnlyDictionary<Guid, MasterFileParameter> NoParameters =
            new ReadOnlyDictionary<Guid, MasterFileParameter>(new Dictionary<Guid, MasterFileParameter>());

        private MasterParameterFile(
            string path, DateTime? writtenAtUtc, IReadOnlyDictionary<Guid, MasterFileParameter> parameters, string problem)
        {
            Path = path;
            WrittenAtUtc = writtenAtUtc;
            Parameters = parameters ?? NoParameters;
            Problem = problem;
        }

        /// <summary>The file as the connection file names it.</summary>
        public string Path { get; }

        /// <summary>When Studio last wrote it, in UTC; null when unknown.</summary>
        public DateTime? WrittenAtUtc { get; }

        /// <summary>Its parameters by GUID; empty when it couldn't be read.</summary>
        public IReadOnlyDictionary<Guid, MasterFileParameter> Parameters { get; }

        /// <summary>Why it couldn't be read, in plain words and naming the file; null when it was read.</summary>
        public string Problem { get; }

        public bool IsRead => Problem == null;

        /// <summary>The file name alone, e.g. "MH_Parameter.txt".</summary>
        public string FileName => string.IsNullOrWhiteSpace(Path) ? "MH_Parameter.txt" : System.IO.Path.GetFileName(Path);

        public bool Holds(Guid guid) => Parameters.ContainsKey(guid);

        /// <summary>
        /// Copies the file to <paramref name="localCopy"/>, then reads the copy. Studio's file is held
        /// open only for the copy, and shared, so Studio can replace it meanwhile; Revit later reads the
        /// copy too. A missing or broken file doesn't throw: <see cref="Problem"/> says what's wrong.
        /// </summary>
        public static MasterParameterFile CopyAndRead(string path, string localCopy)
        {
            if (string.IsNullOrWhiteSpace(localCopy))
                throw new ArgumentException("A local path for the copy is required.", nameof(localCopy));
            if (string.IsNullOrWhiteSpace(path))
                return Failed(path, "The connection file doesn't say where the master shared parameter file is "
                    + "(\"masterParameterFile\").");

            try
            {
                var source = new FileInfo(path);
                if (!source.Exists)
                    return Failed(path, "Can't find the master shared parameter file " + path + ". Studio writes it "
                        + "after signing in; in the office it's on the share, so check that the share is reachable.");
                if (source.Length > MaxFileBytes)
                    return Failed(path, "The file " + path + " is too big to be a shared parameter file.");

                // A connection file naming the copy itself would have it emptied before it's read.
                if (!SamePath(path, localCopy))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localCopy));
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var output = new FileStream(localCopy, FileMode.Create, FileAccess.Write, FileShare.Read))
                        input.CopyTo(output);
                }

                using (var reader = new StreamReader(localCopy, Encoding.UTF8, true))
                    return Read(path, source.LastWriteTimeUtc, reader);
            }
            catch (IOException ex)
            {
                return Failed(path, "Can't read the master shared parameter file " + path + ": " + ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Failed(path, "You don't have permission to read the master shared parameter file " + path + ".");
            }
        }

        /// <summary>Reads a shared parameter file's text. <paramref name="path"/> only names it in problems.</summary>
        public static MasterParameterFile Read(string path, DateTime? writtenAtUtc, TextReader text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            string problem;
            Dictionary<Guid, MasterFileParameter> parameters = SharedParameterText.Parse(text, out problem);
            return problem != null
                ? Failed(path, "The file " + path + " " + problem)
                : new MasterParameterFile(path, writtenAtUtc, new ReadOnlyDictionary<Guid, MasterFileParameter>(parameters), null);
        }

        private static MasterParameterFile Failed(string path, string problem) =>
            new MasterParameterFile(path, null, null, problem);

        private static bool SamePath(string a, string b) =>
            string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}
