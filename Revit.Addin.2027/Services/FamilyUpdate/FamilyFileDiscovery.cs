using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace Revit.Addin._2027.Services.FamilyUpdate
{
    /// <summary>
    /// Finds the .rfa files a run should consider and reads each file's header.
    /// </summary>
    public static class FamilyFileDiscovery
    {
        /// <summary>Revit writes backups as "Chair.0001.rfa" — always exactly four digits.</summary>
        private static readonly Regex RevitBackupPattern =
            new Regex(@"^(?<base>.+)\.\d{4}\.rfa$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Walks the source tree and returns one candidate per family file, header already read.
        /// Folders that cannot be read are skipped rather than aborting the walk, which matters
        /// on network libraries where a single folder may deny access.
        /// </summary>
        /// <param name="excludedRoots">
        /// Folders to stay out of — previous backup folders, and the output folder when it sits
        /// inside the source tree, so a run never picks up its own output.
        /// </param>
        public static IList<FamilyFileCandidate> Discover(
            FamilyUpdateOptions options,
            IEnumerable<string> excludedRoots)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            var exclusions = BuildExclusions(options, excludedRoots);
            var candidates = new List<FamilyFileCandidate>();

            foreach (string filePath in EnumerateFamilyFiles(options.SourceFolder, options.IncludeSubfolders, exclusions))
            {
                if (IsRevitBackupFile(filePath))
                {
                    continue;
                }

                candidates.Add(Inspect(filePath, options.SourceFolder));
            }

            return candidates;
        }

        /// <summary>
        /// Reads the file header without opening the document. This is the cheap check that lets
        /// the runner skip files no enabled step cares about.
        /// </summary>
        private static FamilyFileCandidate Inspect(string filePath, string sourceRoot)
        {
            string relativePath = ToRelativePath(filePath, sourceRoot);
            if (relativePath == null)
            {
                return FamilyFileCandidate.Unreadable(
                    filePath,
                    Path.GetFileName(filePath),
                    "The file could not be located relative to the source folder, so a safe "
                    + "backup and output path cannot be built for it.");
            }

            BasicFileInfo info = null;
            try
            {
                info = BasicFileInfo.Extract(filePath);
                if (info == null)
                {
                    return FamilyFileCandidate.Unreadable(filePath, relativePath, "Revit could not read the file header.");
                }

                long sizeBytes = 0L;
                try
                {
                    sizeBytes = new FileInfo(filePath).Length;
                }
                catch (Exception)
                {
                    // Size is cosmetic; a failure here must not disqualify the file.
                }

                return FamilyFileCandidate.Readable(
                    filePath,
                    relativePath,
                    sizeBytes,
                    info.Format,
                    info.IsSavedInCurrentVersion,
                    info.IsSavedInLaterVersion);
            }
            catch (Exception ex)
            {
                return FamilyFileCandidate.Unreadable(filePath, relativePath, ex.Message);
            }
            finally
            {
                if (info != null)
                {
                    try
                    {
                        info.Dispose();
                    }
                    catch (Exception)
                    {
                        // Nothing useful to do if Revit fails to release the header handle.
                    }
                }
            }
        }

        /// <summary>
        /// Iterative walk so that an unreadable subfolder skips that branch instead of throwing
        /// out of the whole enumeration, which is what Directory.GetFiles(AllDirectories) does.
        /// </summary>
        private static IEnumerable<string> EnumerateFamilyFiles(
            string root,
            bool includeSubfolders,
            ICollection<string> exclusions)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory, "*.rfa");
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }

                if (!includeSubfolders)
                {
                    continue;
                }

                string[] subDirectories;
                try
                {
                    subDirectories = Directory.GetDirectories(directory);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string subDirectory in subDirectories)
                {
                    if (!IsExcluded(subDirectory, exclusions))
                    {
                        pending.Push(subDirectory);
                    }
                }
            }
        }

        private static HashSet<string> BuildExclusions(FamilyUpdateOptions options, IEnumerable<string> excludedRoots)
        {
            var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (excludedRoots != null)
            {
                foreach (string root in excludedRoots)
                {
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        exclusions.Add(root);
                    }
                }
            }

            // Backup folders from earlier runs hold the pre-upgrade originals on purpose.
            // Re-processing them would defeat the point of keeping them.
            try
            {
                foreach (string directory in Directory.GetDirectories(
                             options.SourceFolder,
                             FamilyUpdateOptions.BackupFolderPrefix + "*"))
                {
                    exclusions.Add(directory);
                }
            }
            catch (Exception)
            {
                // If the source root cannot be listed the walk below will report it anyway.
            }

            return exclusions;
        }

        private static bool IsExcluded(string directory, ICollection<string> exclusions)
        {
            foreach (string exclusion in exclusions)
            {
                if (FamilyUpdateOptions.IsInside(directory, exclusion))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A file is a Revit backup only when it matches the four-digit pattern *and* the family
        /// it backs up sits next to it. That second check keeps a legitimately named family such
        /// as "Fenster.2024.rfa" out of the exclusion.
        /// </summary>
        private static bool IsRevitBackupFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName == null)
            {
                return false;
            }

            Match match = RevitBackupPattern.Match(fileName);
            if (!match.Success)
            {
                return false;
            }

            string directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            string originalPath = Path.Combine(directory, match.Groups["base"].Value + ".rfa");
            return File.Exists(originalPath);
        }

        /// <summary>
        /// Path relative to the source root, or null when it cannot be determined.
        /// </summary>
        /// <remarks>
        /// Falling back to the bare file name would be actively dangerous: the relative path is
        /// what places a file inside the backup and output folders, so two families with the same
        /// name in different subfolders would collapse onto one destination and silently
        /// overwrite each other's backup. A file we cannot place is reported instead.
        /// </remarks>
        private static string ToRelativePath(string filePath, string root)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                string fullFile = Path.GetFullPath(filePath);

                if (fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return fullFile.Substring(fullRoot.Length);
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
