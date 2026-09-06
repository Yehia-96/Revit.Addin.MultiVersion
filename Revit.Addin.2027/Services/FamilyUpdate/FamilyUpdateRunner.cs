using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace Revit.Addin._2027.Services.FamilyUpdate
{
    /// <summary>Reports progress between files so the UI can stay responsive.</summary>
    public delegate void FamilyUpdateProgressCallback(int completed, int total, string currentFileName);

    /// <summary>
    /// Runs the configured steps over every family file in a directory.
    /// </summary>
    /// <remarks>
    /// Deliberately holds no transaction against the active project. Each .rfa is opened as its
    /// own standalone document, processed, written and closed; the project the user happens to
    /// have open is never touched, and the command does not even require one.
    /// </remarks>
    public sealed class FamilyUpdateRunner
    {
        private readonly RevitApp _application;
        private readonly FamilyUpdateOptions _options;
        private readonly IList<IFamilyFileStep> _steps;

        public FamilyUpdateRunner(RevitApp application, FamilyUpdateOptions options, IEnumerable<IFamilyFileStep> steps)
        {
            if (application == null)
            {
                throw new ArgumentNullException("application");
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (steps == null)
            {
                throw new ArgumentNullException("steps");
            }

            _application = application;
            _options = options;
            _steps = steps.Where(s => s != null).ToList();

            if (_steps.Count == 0)
            {
                throw new ArgumentException("At least one step must be enabled.", "steps");
            }

            StartedAt = DateTime.Now;
            BackupRoot = _options.OutputMode == FamilyOutputMode.InPlaceWithBackup
                ? _options.BuildBackupRoot(StartedAt)
                : null;
        }

        public DateTime StartedAt { get; private set; }

        /// <summary>Timestamped folder holding the untouched originals. Null unless updating in place.</summary>
        public string BackupRoot { get; private set; }

        /// <summary>Where the CSV report is written — next to the backups, or next to the output.</summary>
        public string ReportFolder
        {
            get
            {
                return _options.OutputMode == FamilyOutputMode.InPlaceWithBackup
                    ? BackupRoot
                    : _options.OutputFolder;
            }
        }

        public IList<FamilyFileCandidate> Discover()
        {
            var excludedRoots = new List<string>();

            if (BackupRoot != null)
            {
                excludedRoots.Add(BackupRoot);
            }

            if (_options.OutputMode == FamilyOutputMode.CopyToSeparateFolder
                && !string.IsNullOrWhiteSpace(_options.OutputFolder))
            {
                excludedRoots.Add(_options.OutputFolder);
            }

            return FamilyFileDiscovery.Discover(_options, excludedRoots);
        }

        public FamilyUpdateReport Run(
            IList<FamilyFileCandidate> candidates,
            FamilyUpdateProgressCallback onProgress,
            Func<bool> isCancellationRequested)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException("candidates");
            }

            var report = new FamilyUpdateReport(StartedAt, _options);
            int total = candidates.Count;
            int completed = 0;

            // By the time anything can go wrong here, files have already been rewritten on disk.
            // The report is the user's only record of what changed, so it is returned whatever
            // happens rather than being lost with the exception.
            try
            {
                foreach (FamilyFileCandidate candidate in candidates)
                {
                    if (IsCancelled(isCancellationRequested))
                    {
                        report.WasCancelled = true;
                        break;
                    }

                    ReportProgress(onProgress, completed, total, candidate.RelativePath);

                    report.Add(ProcessFile(candidate));
                    completed++;
                }
            }
            catch (Exception ex)
            {
                report.AbortError = ex.Message;
            }

            ReportProgress(onProgress, completed, total, string.Empty);

            return report;
        }

        /// <summary>
        /// Progress reporting is cosmetic. A failure in the UI callback must not discard a run
        /// that has already written files.
        /// </summary>
        private static void ReportProgress(
            FamilyUpdateProgressCallback onProgress,
            int completed,
            int total,
            string currentFileName)
        {
            if (onProgress == null)
            {
                return;
            }

            try
            {
                onProgress(completed, total, currentFileName);
            }
            catch (Exception)
            {
                // Losing a progress update is preferable to losing the run.
            }
        }

        /// <summary>A callback that throws is treated as "keep going" rather than aborting.</summary>
        private static bool IsCancelled(Func<bool> isCancellationRequested)
        {
            if (isCancellationRequested == null)
            {
                return false;
            }

            try
            {
                return isCancellationRequested();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private FamilyUpdateResult ProcessFile(FamilyFileCandidate candidate)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!candidate.IsReadable)
            {
                return Failed(candidate, null, "Could not read file header: " + candidate.InspectionError, stopwatch);
            }

            if (candidate.IsSavedInLaterVersion)
            {
                return Skipped(
                    candidate,
                    string.Format(
                        "Saved in a newer Revit ({0}) than the one running ({1}); it cannot be opened here.",
                        candidate.VersionLabel,
                        _application.VersionNumber),
                    stopwatch);
            }

            List<IFamilyFileStep> applicableSteps;
            try
            {
                // A third-party step deciding whether it applies must not be able to take the
                // whole batch down with it.
                applicableSteps = _steps.Where(step => step.ShouldProcess(candidate)).ToList();
            }
            catch (Exception ex)
            {
                return Failed(candidate, null, "A step failed while inspecting the file: " + ex.Message, stopwatch);
            }

            if (applicableSteps.Count == 0)
            {
                return Skipped(
                    candidate,
                    string.Format("Nothing to do (already format {0}).", candidate.VersionLabel),
                    stopwatch);
            }

            string targetPath;
            try
            {
                targetPath = ResolveTargetPath(candidate);
            }
            catch (Exception ex)
            {
                return Failed(candidate, null, "Could not resolve the output path: " + ex.Message, stopwatch);
            }

            // The backup has to be on disk before the original is overwritten. If it fails the
            // file is left exactly as it was rather than being updated unprotected.
            if (_options.OutputMode == FamilyOutputMode.InPlaceWithBackup)
            {
                string backupError = TryBackup(candidate);
                if (backupError != null)
                {
                    return Failed(candidate, targetPath, "Backup failed, file left unchanged: " + backupError, stopwatch);
                }
            }

            Document familyDocument = null;
            try
            {
                familyDocument = OpenFamilyDocument(candidate.Path);

                if (!familyDocument.IsFamilyDocument)
                {
                    return Failed(candidate, targetPath, "The file is not a Revit family document.", stopwatch);
                }

                var context = new FamilyFileContext(candidate, familyDocument, _application, targetPath);
                var messages = new List<string>();
                bool requiresSave = false;

                foreach (IFamilyFileStep step in applicableSteps)
                {
                    FamilyStepOutcome outcome = step.Apply(context);

                    if (outcome.Status == FamilyStepStatus.Failed)
                    {
                        return Failed(
                            candidate,
                            targetPath,
                            string.Format("Step '{0}' failed: {1}", step.Name, outcome.Message),
                            stopwatch);
                    }

                    if (!string.IsNullOrEmpty(outcome.Message))
                    {
                        messages.Add(outcome.Message);
                    }

                    if (outcome.Status == FamilyStepStatus.RequiresSave)
                    {
                        requiresSave = true;
                    }
                }

                if (!requiresSave)
                {
                    return Skipped(candidate, "No step required a save.", stopwatch);
                }

                SaveFamilyDocument(familyDocument, targetPath);

                if (_options.CopyTypeCatalogs)
                {
                    string catalogNote = TryCopyTypeCatalog(candidate, targetPath);
                    if (catalogNote != null)
                    {
                        messages.Add(catalogNote);
                    }
                }

                return new FamilyUpdateResult(
                    candidate.Path,
                    targetPath,
                    FamilyUpdateStatus.Updated,
                    string.Join(" ", messages),
                    stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                return Failed(candidate, targetPath, ex.Message, stopwatch);
            }
            finally
            {
                CloseQuietly(familyDocument);
            }
        }

        private Document OpenFamilyDocument(string path)
        {
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
            var openOptions = new OpenOptions { Audit = _options.AuditOnOpen };

            // Opening an older .rfa in this Revit build is what performs the version upgrade;
            // it happens in memory and leaves the file on disk untouched until it is saved.
            return _application.OpenDocumentFile(modelPath, openOptions);
        }

        private void SaveFamilyDocument(Document familyDocument, string targetPath)
        {
            string targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // SaveAs is used for both output modes, including when the target is the file the
            // document was opened from: with OverwriteExistingFile set, that is the documented
            // way to overwrite. Save() is not equivalent here — it can no-op on a document Revit
            // considers unmodified, which would silently defeat a forced re-save.
            // MaximumBackups of 1 stops Revit from scattering .0001.rfa files through the
            // library; the originals are already preserved by the backup or output folder.
            var saveOptions = new SaveAsOptions
            {
                OverwriteExistingFile = true,
                Compact = _options.CompactOnSave,
                MaximumBackups = 1
            };

            familyDocument.SaveAs(targetPath, saveOptions);
        }

        private string ResolveTargetPath(FamilyFileCandidate candidate)
        {
            if (_options.OutputMode == FamilyOutputMode.InPlaceWithBackup)
            {
                return candidate.Path;
            }

            // Mirror the source structure and keep the original file name: a family's name comes
            // from its file name, so renaming here would make every reload create a duplicate
            // family in the project instead of updating the existing one.
            return Path.Combine(_options.OutputFolder, candidate.RelativePath);
        }

        /// <summary>Returns null on success, otherwise the reason the backup could not be made.</summary>
        private string TryBackup(FamilyFileCandidate candidate)
        {
            try
            {
                string backupPath = Path.Combine(BackupRoot, candidate.RelativePath);
                string backupDirectory = Path.GetDirectoryName(backupPath);

                if (!string.IsNullOrEmpty(backupDirectory))
                {
                    Directory.CreateDirectory(backupDirectory);
                }

                File.Copy(candidate.Path, backupPath, true);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Brings a family's type catalog along to a new location. Without the sibling .txt file
        /// the family loses its type list when it is loaded from there.
        /// </summary>
        private string TryCopyTypeCatalog(FamilyFileCandidate candidate, string targetPath)
        {
            if (_options.OutputMode != FamilyOutputMode.CopyToSeparateFolder)
            {
                return null;
            }

            try
            {
                string sourceDirectory = Path.GetDirectoryName(candidate.Path);
                if (string.IsNullOrEmpty(sourceDirectory))
                {
                    return null;
                }

                string catalogName = Path.GetFileNameWithoutExtension(candidate.Path) + ".txt";
                string sourceCatalog = Path.Combine(sourceDirectory, catalogName);

                if (!File.Exists(sourceCatalog))
                {
                    return null;
                }

                string targetDirectory = Path.GetDirectoryName(targetPath);
                if (string.IsNullOrEmpty(targetDirectory))
                {
                    return null;
                }

                File.Copy(sourceCatalog, Path.Combine(targetDirectory, catalogName), true);
                return "Type catalog copied.";
            }
            catch (Exception ex)
            {
                return "Type catalog could not be copied: " + ex.Message + ".";
            }
        }

        /// <summary>
        /// Always runs, including on the exception path. A family document left open holds a
        /// lock on the file and blocks every later run in the same Revit session.
        /// </summary>
        private static void CloseQuietly(Document familyDocument)
        {
            if (familyDocument == null)
            {
                return;
            }

            try
            {
                if (familyDocument.IsValidObject)
                {
                    familyDocument.Close(false);
                }
            }
            catch (Exception)
            {
                // Nothing actionable; the run must continue with the remaining files.
            }
        }

        private static FamilyUpdateResult Skipped(FamilyFileCandidate candidate, string message, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            return new FamilyUpdateResult(
                candidate.Path,
                null,
                FamilyUpdateStatus.Skipped,
                message,
                stopwatch.Elapsed);
        }

        private static FamilyUpdateResult Failed(
            FamilyFileCandidate candidate,
            string targetPath,
            string message,
            Stopwatch stopwatch)
        {
            stopwatch.Stop();
            return new FamilyUpdateResult(
                candidate.Path,
                targetPath,
                FamilyUpdateStatus.Failed,
                message,
                stopwatch.Elapsed);
        }
    }
}
