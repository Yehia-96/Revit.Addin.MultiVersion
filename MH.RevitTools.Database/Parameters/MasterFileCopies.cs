using System;
using System.Globalization;
using System.IO;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// Where each Revit keeps its copy of the master file: one per Revit process, so two Revits open at
    /// once never write over the copy the other is reading. Old copies are cleared away.
    /// </summary>
    public static class MasterFileCopies
    {
        private const string Prefix = "MH_Parameter.";

        /// <summary>E.g. <c>...\MH\RevitTools\MH_Parameter.12345.txt</c> for the Revit with process id 12345.</summary>
        public static string PathFor(string folder, int processId) =>
            Path.Combine(folder, Prefix + processId.ToString(CultureInfo.InvariantCulture) + ".txt");

        /// <summary>
        /// Deletes copies not written for longer than <paramref name="maxAge"/>. Each Check parameters
        /// copies the file afresh before reading it, so an old copy is never needed again. Best effort:
        /// a copy in use or already gone just stays or is skipped. Returns how many were deleted.
        /// </summary>
        public static int RemoveOld(string folder, DateTime nowUtc, TimeSpan maxAge)
        {
            if (!Directory.Exists(folder))
                return 0;

            int removed = 0;
            foreach (string file in Directory.GetFiles(folder, Prefix + "*.txt"))
            {
                try
                {
                    if (nowUtc - File.GetLastWriteTimeUtc(file) <= maxAge)
                        continue;
                    File.Delete(file);
                    removed++;
                }
                catch (IOException)
                {
                    // In use by a running Revit, or deleted meanwhile: it's cleared next time.
                }
                catch (UnauthorizedAccessException)
                {
                    // Not ours to delete: leave it.
                }
            }
            return removed;
        }
    }
}
