using System;
using System.Globalization;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// How a link's time is kept in the model, which can't store a date as such: UTC as ISO 8601 text,
    /// e.g. "2026-11-19T14:32:05Z", the same on every PC whatever its language.
    /// </summary>
    public static class StoredTime
    {
        private const string Pattern = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        /// <summary>The text to store; empty for an unknown time.</summary>
        public static string ToText(DateTime? utc)
        {
            if (!utc.HasValue)
                return "";
            DateTime value = utc.Value.Kind == DateTimeKind.Local ? utc.Value.ToUniversalTime() : utc.Value;
            return value.ToString(Pattern, CultureInfo.InvariantCulture);
        }

        /// <summary>The time in UTC; null for empty or unreadable text.</summary>
        public static DateTime? FromText(string text)
        {
            DateTime value;
            bool read = DateTime.TryParseExact(
                (text ?? "").Trim(),
                Pattern,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value);
            return read ? value : (DateTime?)null;
        }
    }
}
