using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit.Addin._2026.Services.FamilyUpdate
{
    public enum FamilyUpdateStatus
    {
        Updated = 0,
        Skipped = 1,
        Failed = 2
    }

    /// <summary>Outcome for a single family file.</summary>
    public sealed class FamilyUpdateResult
    {
        public FamilyUpdateResult(
            string sourcePath,
            string targetPath,
            FamilyUpdateStatus status,
            string message,
            TimeSpan duration)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            Status = status;
            Message = message ?? string.Empty;
            Duration = duration;
        }

        public string SourcePath { get; private set; }

        public string TargetPath { get; private set; }

        public FamilyUpdateStatus Status { get; private set; }

        public string Message { get; private set; }

        public TimeSpan Duration { get; private set; }
    }

    /// <summary>
    /// Collects per-file outcomes and turns them into something the user can act on: a summary
    /// for the closing dialog and a CSV listing every file, so a failed batch can be diagnosed
    /// instead of guessed at.
    /// </summary>
    public sealed class FamilyUpdateReport
    {
        private readonly List<FamilyUpdateResult> _results = new List<FamilyUpdateResult>();

        public FamilyUpdateReport(DateTime startedAt, FamilyUpdateOptions options)
        {
            StartedAt = startedAt;
            Options = options;
        }

        public DateTime StartedAt { get; private set; }

        public FamilyUpdateOptions Options { get; private set; }

        public bool WasCancelled { get; set; }

        /// <summary>
        /// Set when the batch loop itself stopped on an unexpected error. The results collected
        /// up to that point are still valid and still describe files that were written.
        /// </summary>
        public string AbortError { get; set; }

        public IReadOnlyList<FamilyUpdateResult> Results
        {
            get { return _results; }
        }

        public int TotalCount
        {
            get { return _results.Count; }
        }

        public int UpdatedCount
        {
            get { return _results.Count(r => r.Status == FamilyUpdateStatus.Updated); }
        }

        public int SkippedCount
        {
            get { return _results.Count(r => r.Status == FamilyUpdateStatus.Skipped); }
        }

        public int FailedCount
        {
            get { return _results.Count(r => r.Status == FamilyUpdateStatus.Failed); }
        }

        public void Add(FamilyUpdateResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            _results.Add(result);
        }

        public string BuildSummary()
        {
            var builder = new StringBuilder();

            if (WasCancelled)
            {
                builder.AppendLine("Run cancelled — files already processed were written and are listed below.");
                builder.AppendLine();
            }

            if (!string.IsNullOrEmpty(AbortError))
            {
                builder.AppendLine("The run stopped early: " + AbortError);
                builder.AppendLine("Files processed before that point were written and are listed below.");
                builder.AppendLine();
            }

            builder.AppendLine(string.Format("Files examined: {0}", TotalCount));
            builder.AppendLine(string.Format("Updated:        {0}", UpdatedCount));
            builder.AppendLine(string.Format("Skipped:        {0}", SkippedCount));
            builder.AppendLine(string.Format("Failed:         {0}", FailedCount));

            if (FailedCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine("First failures:");

                foreach (FamilyUpdateResult failure in _results
                             .Where(r => r.Status == FamilyUpdateStatus.Failed)
                             .Take(5))
                {
                    builder.AppendLine(string.Format(
                        "  • {0}: {1}",
                        Path.GetFileName(failure.SourcePath),
                        failure.Message));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Writes the full result list as CSV and returns the path, or null when it could not
        /// be written. A failure here must never mask the result of the run itself.
        /// </summary>
        public string TryWriteCsv(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(folder);

                string fileName = string.Format(
                    "FamilyUpdate_{0}.csv",
                    StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                string path = Path.Combine(folder, fileName);

                var builder = new StringBuilder();
                builder.AppendLine("Status,SourcePath,TargetPath,Seconds,Message");

                foreach (FamilyUpdateResult result in _results)
                {
                    builder.AppendLine(string.Join(",",
                        Csv(result.Status.ToString()),
                        Csv(result.SourcePath),
                        Csv(result.TargetPath),
                        Csv(result.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)),
                        Csv(result.Message)));
                }

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
