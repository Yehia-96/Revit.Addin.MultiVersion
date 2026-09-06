using System;
using System.IO;

namespace Revit.Addin._2026.Services.FamilyUpdate
{
    /// <summary>
    /// Everything the user chooses in the options dialog before a batch run starts.
    /// </summary>
    public sealed class FamilyUpdateOptions
    {
        /// <summary>Backup folders are created under the source root with this prefix.</summary>
        public const string BackupFolderPrefix = "_FamilyUpdate_Backup_";

        /// <summary>Root of the library to process.</summary>
        public string SourceFolder { get; set; }

        /// <summary>Walk nested folders. A real family library is almost always a tree.</summary>
        public bool IncludeSubfolders { get; set; }

        /// <summary>
        /// Leave files that are already saved in the running release untouched. This is the
        /// difference between minutes and hours on a large library.
        /// </summary>
        public bool SkipFilesAlreadyCurrent { get; set; }

        /// <summary>Run Revit's audit while opening. Much slower, but repairs corrupt files.</summary>
        public bool AuditOnOpen { get; set; }

        /// <summary>Compact on save. Slower per file, but reclaims space in long-lived families.</summary>
        public bool CompactOnSave { get; set; }

        /// <summary>
        /// Copy a family's type catalog (the sibling .txt file) alongside it. Without the
        /// catalog a family loses its type list when it is loaded from the new location.
        /// </summary>
        public bool CopyTypeCatalogs { get; set; }

        public FamilyOutputMode OutputMode { get; set; }

        /// <summary>Destination root, used only by <see cref="FamilyOutputMode.CopyToSeparateFolder"/>.</summary>
        public string OutputFolder { get; set; }

        public static FamilyUpdateOptions CreateDefault()
        {
            return new FamilyUpdateOptions
            {
                IncludeSubfolders = true,
                SkipFilesAlreadyCurrent = true,
                AuditOnOpen = false,
                CompactOnSave = true,
                CopyTypeCatalogs = true,
                OutputMode = FamilyOutputMode.InPlaceWithBackup
            };
        }

        /// <summary>
        /// Returns null when the options are usable, otherwise a message to show the user.
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceFolder))
            {
                return "Select the source folder that holds the family files.";
            }

            if (!Directory.Exists(SourceFolder))
            {
                return string.Format("The source folder does not exist:{0}{1}", Environment.NewLine, SourceFolder);
            }

            if (OutputMode != FamilyOutputMode.CopyToSeparateFolder)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                return "Select the output folder for the upgraded copies.";
            }

            if (PathsPointToSameFolder(SourceFolder, OutputFolder))
            {
                return "The output folder must be different from the source folder. "
                     + "Use \"Update in place\" if you want to overwrite the originals.";
            }

            // An output folder nested inside the source tree is allowed — the runner excludes it
            // from discovery so a second run never picks up its own output.
            return null;
        }

        /// <summary>
        /// Absolute path of the timestamped backup folder for a run. Only meaningful for
        /// <see cref="FamilyOutputMode.InPlaceWithBackup"/>.
        /// </summary>
        public string BuildBackupRoot(DateTime startedAt)
        {
            string folderName = BackupFolderPrefix + startedAt.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(SourceFolder, folderName);
        }

        /// <summary>True when <paramref name="candidate"/> sits inside <paramref name="root"/>.</summary>
        public static bool IsInside(string candidate, string root)
        {
            string normalizedCandidate = NormalizeFolder(candidate);
            string normalizedRoot = NormalizeFolder(root);

            if (normalizedCandidate == null || normalizedRoot == null)
            {
                return false;
            }

            return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static bool PathsPointToSameFolder(string first, string second)
        {
            string normalizedFirst = NormalizeFolder(first);
            string normalizedSecond = NormalizeFolder(second);

            if (normalizedFirst == null || normalizedSecond == null)
            {
                return false;
            }

            return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Full path with a single trailing separator, so that prefix comparisons cannot match
        /// a sibling folder whose name merely starts with the same characters.
        /// </summary>
        private static string NormalizeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(path.Trim());
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
