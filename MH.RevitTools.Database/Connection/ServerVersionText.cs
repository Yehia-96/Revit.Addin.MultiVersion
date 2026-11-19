using System;

namespace MH.RevitTools.Database.Connection
{
    /// <summary>Turns @@VERSION and SERVERPROPERTY('ProductVersion') into "SQL Server 2019 (15.0.4382.1)".</summary>
    internal static class ServerVersionText
    {
        private const string Vendor = "Microsoft ";

        internal static string Describe(string versionText, string productVersion)
        {
            string product = ProductName(versionText);
            bool hasNumber = !string.IsNullOrWhiteSpace(productVersion);
            if (product != null && hasNumber)
                return product + " (" + productVersion.Trim() + ")";
            if (product != null)
                return product;
            if (hasNumber)
                return "SQL Server " + productVersion.Trim();
            return "SQL Server (version unknown)";
        }

        // "Microsoft SQL Server 2019 (RTM-CU32-GDR) (KB…) - 15.0.4382.1 (X64) …" → "SQL Server 2019"
        private static string ProductName(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
                return null;

            string line = versionText.Split(new[] { '\r', '\n' }, 2)[0];
            int end = line.IndexOf(" (", StringComparison.Ordinal);
            if (end < 0)
                end = line.IndexOf(" - ", StringComparison.Ordinal);

            string name = (end < 0 ? line : line.Substring(0, end)).Trim();
            if (name.StartsWith(Vendor, StringComparison.Ordinal))
                name = name.Substring(Vendor.Length);
            return name.Length == 0 ? null : name;
        }
    }
}
