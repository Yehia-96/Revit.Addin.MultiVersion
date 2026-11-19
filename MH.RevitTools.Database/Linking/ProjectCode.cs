using System;
using System.Globalization;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// How models and the add-in's dialogs write a project: <c>1761-01</c> with a sub-number, <c>1761</c>
    /// without one (a sub-number of 0).
    /// </summary>
    public static class ProjectCode
    {
        public static string Format(int number, int subNumber)
        {
            string main = number.ToString("0000", CultureInfo.InvariantCulture);
            return subNumber > 0 ? main + "-" + subNumber.ToString("00", CultureInfo.InvariantCulture) : main;
        }

        public static string Format(LinkProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            return Format(project.Number, project.SubNumber);
        }

        /// <summary>The code and the name, e.g. "1761-01 Ulm".</summary>
        public static string Describe(int number, int subNumber, string name)
        {
            string code = Format(number, subNumber);
            return string.IsNullOrWhiteSpace(name) ? code : code + " " + name.Trim();
        }

        public static string Describe(LinkProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            return Describe(project.Number, project.SubNumber, project.Name);
        }
    }
}
