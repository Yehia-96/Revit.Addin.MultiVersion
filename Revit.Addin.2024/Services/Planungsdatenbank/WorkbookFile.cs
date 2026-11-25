// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
// Without Revit, so it's under tests: MH.RevitTools.Database.Tests\ExcelImport.
using System;
using System.IO;

namespace Revit.Addin._2024.Services.Planungsdatenbank
{
    /// <summary>
    /// A workbook as it was when it was read: where it is, and when it had been saved.
    /// <para>
    /// It's read from a copy, so it may stay open in Excel. Excel locks the file it has open, and the one who changes a
    /// value and runs the tool again usually still has it open.
    /// </para>
    /// </summary>
    internal sealed class WorkbookFile
    {
        private readonly DateTime _savedUtc;
        private readonly long _length;

        private WorkbookFile(string path, DateTime savedUtc, long length)
        {
            FullPath = path;
            _savedUtc = savedUtc;
            _length = length;
        }

        internal string FullPath { get; private set; }

        /// <summary>E.g. "Planungsdatenbank.xlsx, saved 25.11.2026 10:48".</summary>
        internal string Description
        {
            get { return Path.GetFileName(FullPath) + ", saved " + _savedUtc.ToLocalTime().ToString("g"); }
        }

        /// <summary>True when the file was saved again since it was read. Not when it's gone: then there's nothing to read.</summary>
        internal bool HasChanged()
        {
            var now = new FileInfo(FullPath);
            return now.Exists && (now.LastWriteTimeUtc != _savedUtc || now.Length != _length);
        }

        /// <summary>Copies the workbook, has the copy read, and deletes it.</summary>
        /// <param name="read">Reads the workbook at the path it's given. What it throws, this throws.</param>
        /// <exception cref="FileNotFoundException">There's no such file.</exception>
        internal static WorkbookFile Read(string path, Action<string> read)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty.", "path");
            if (read == null) throw new ArgumentNullException("read");

            // Before the copy: a save in between then counts as a change, and the next run reads it.
            var original = new FileInfo(path);
            if (!original.Exists) throw new FileNotFoundException("Workbook not found.", path);
            var file = new WorkbookFile(path, original.LastWriteTimeUtc, original.Length);

            string copy = Path.Combine(Path.GetTempPath(), "MH.RevitTools." + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            try
            {
                using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream target = File.Create(copy))
                    source.CopyTo(target);

                read(copy);
            }
            finally
            {
                Delete(copy);
            }
            return file;
        }

        // A copy that can't be deleted only stays in the temp folder.
        private static void Delete(string copy)
        {
            try
            {
                if (File.Exists(copy)) File.Delete(copy);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
