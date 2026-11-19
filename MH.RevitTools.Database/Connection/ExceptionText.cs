using System;
using System.Text;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>
    /// The technical side of a failure, for a dialog's details: each exception's type and message,
    /// outermost first, without stack traces. Must not touch SqlClient types, for the same reason
    /// as <see cref="ConnectionProblems.DescribeDriverFailure"/>.
    /// </summary>
    internal static class ExceptionText
    {
        private const int MaxDepth = 6;

        internal static string Describe(Exception error, string note)
        {
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(note))
                text.AppendLine(note);

            int depth = 0;
            for (Exception e = error; e != null && depth < MaxDepth; e = e.InnerException, depth++)
                text.Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);

            return text.ToString().TrimEnd();
        }
    }
}
