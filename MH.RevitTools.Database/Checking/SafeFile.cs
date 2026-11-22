using System;
using System.IO;
using System.Text;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// Writes a file whole or not at all: first to a temporary file beside it, then in its place. A Revit
    /// that stops half-way, or a second Revit reading at that moment, never sees half a file.
    /// </summary>
    internal static class SafeFile
    {
        /// <summary>Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't.</summary>
        public static void WriteAllText(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        // Left only when writing failed half-way; a leftover is harmless, so a failure here isn't reported.
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
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
