using System;

namespace Revit.Addin._2026.Services.FamilyUpdate
{
    /// <summary>
    /// One .rfa file found on disk, together with the header information Revit can read
    /// without opening the document. Steps use this to decide — cheaply — whether a file
    /// is worth opening at all.
    /// </summary>
    public sealed class FamilyFileCandidate
    {
        private FamilyFileCandidate(
            string path,
            string relativePath,
            long sizeBytes,
            string savedInFormat,
            bool isSavedInCurrentVersion,
            bool isSavedInLaterVersion,
            string inspectionError)
        {
            Path = path;
            RelativePath = relativePath;
            SizeBytes = sizeBytes;
            SavedInFormat = savedInFormat;
            IsSavedInCurrentVersion = isSavedInCurrentVersion;
            IsSavedInLaterVersion = isSavedInLaterVersion;
            InspectionError = inspectionError;
        }

        /// <summary>Absolute path of the source file.</summary>
        public string Path { get; private set; }

        /// <summary>Path relative to the source root, used to mirror the folder structure.</summary>
        public string RelativePath { get; private set; }

        public long SizeBytes { get; private set; }

        /// <summary>Revit release the file was last saved in, e.g. "2021". Empty when unknown.</summary>
        public string SavedInFormat { get; private set; }

        public bool IsSavedInCurrentVersion { get; private set; }

        /// <summary>True when the file comes from a newer Revit and therefore cannot be opened here.</summary>
        public bool IsSavedInLaterVersion { get; private set; }

        /// <summary>Non-null when the file header could not be read; the file is then skipped.</summary>
        public string InspectionError { get; private set; }

        public bool IsReadable
        {
            get { return string.IsNullOrEmpty(InspectionError); }
        }

        public string VersionLabel
        {
            get { return string.IsNullOrEmpty(SavedInFormat) ? "unknown" : SavedInFormat; }
        }

        public static FamilyFileCandidate Readable(
            string path,
            string relativePath,
            long sizeBytes,
            string savedInFormat,
            bool isSavedInCurrentVersion,
            bool isSavedInLaterVersion)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path");
            }

            return new FamilyFileCandidate(
                path,
                relativePath,
                sizeBytes,
                savedInFormat,
                isSavedInCurrentVersion,
                isSavedInLaterVersion,
                null);
        }

        public static FamilyFileCandidate Unreadable(string path, string relativePath, string error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path");
            }

            return new FamilyFileCandidate(
                path,
                relativePath,
                0L,
                string.Empty,
                false,
                false,
                string.IsNullOrWhiteSpace(error) ? "Unknown inspection error." : error);
        }
    }
}
