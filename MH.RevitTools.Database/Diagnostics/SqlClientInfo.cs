using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;
#if NET
using System.Runtime.Loader;
#endif

namespace MH.RevitTools.Database.Diagnostics
{
    /// <summary>
    /// Which database driver this process actually loaded, and from where: the first thing to look
    /// at when a connection fails inside Revit. Never throws.
    /// </summary>
    public static class SqlClientInfo
    {
        private const string NativePartPrefix = "Microsoft.Data.SqlClient.SNI";

        public static string Describe()
        {
            return "Driver: " + Safely(DescribeDriver) + Environment.NewLine
                + "Native part: " + Safely(DescribeNativePart) + Environment.NewLine
                + "Runtime: " + RuntimeInformation.FrameworkDescription;
        }

        // Called through a delegate, so a SqlClient that can't load fails inside the try below.
        private static string Safely(Func<string> describe)
        {
            try
            {
                return describe();
            }
            catch (Exception ex)
            {
                return "unknown (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
        }

        private static string DescribeDriver()
        {
            Assembly driver = typeof(SqlConnection).Assembly;
            string text = "Microsoft.Data.SqlClient " + VersionOf(driver) + " from " + driver.Location;
#if NET
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(driver);
            if (context != null)
                text += " (load context: " + context.Name + ")";
#endif
            return text;
        }

        private static string VersionOf(Assembly assembly)
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version = informational != null
                ? informational.InformationalVersion
                : assembly.GetName().Version.ToString();
            int plus = version.IndexOf('+');   // some builds append the commit
            return plus < 0 ? version : version.Substring(0, plus);
        }

        // SqlClient's native networking part, if loaded. It isn't before the first connection, nor
        // ever when SqlClient uses its managed networking instead.
        private static string DescribeNativePart()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (module.ModuleName.StartsWith(NativePartPrefix, StringComparison.OrdinalIgnoreCase))
                        return module.FileName;
                }
            }
            return "not loaded";
        }
    }
}
