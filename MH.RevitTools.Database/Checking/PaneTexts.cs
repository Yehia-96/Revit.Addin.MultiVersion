using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Changes;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// What the MH Database pane says, in the mockup's words, so the wording sits under tests and the Revit
    /// side only places it. Times are shown in the PC's time zone: "14:28" today, "yesterday 14:28", else
    /// "2026-11-21 14:28".
    /// </summary>
    public static class PaneTexts
    {
        private const string Clock = "HH:mm";
        private const string Day = "yyyy-MM-dd";

        /// <summary>A time as the pane shows it, relative to now.</summary>
        public static string When(DateTime utc, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            TimeZoneInfo zone = timeZone ?? TimeZoneInfo.Utc;
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone);
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone).Date;
            string clock = local.ToString(Clock, CultureInfo.InvariantCulture);
            if (local.Date == today)
                return clock;
            if (local.Date == today.AddDays(-1))
                return "yesterday " + clock;
            return local.ToString(Day, CultureInfo.InvariantCulture) + " " + clock;
        }

        /// <summary>As <see cref="When"/>, with "today" spelled out for a whole sentence: "today 14:31".</summary>
        public static string WhenInSentence(DateTime utc, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            string when = When(utc, nowUtc, timeZone);
            return char.IsDigit(when[0]) && when.Length == Clock.Length ? "today " + when : when;
        }

        /// <summary>The header's second line while the database answers, e.g. "Checked 14:32 · every minute".</summary>
        public static string Checked(DateTime checkedAtUtc, DateTime nowUtc, TimeZoneInfo timeZone, CheckInterval interval) =>
            "Checked " + When(checkedAtUtc, nowUtc, timeZone) + " · " + (interval ?? CheckInterval.For(CheckSettings.DefaultSeconds)).Phrase;

        /// <summary>The header's second line while it doesn't, e.g. "Last answer 11:05 · tries again every minute".</summary>
        public static string NoAnswer(DateTime? lastAnswerUtc, DateTime nowUtc, TimeZoneInfo timeZone, CheckInterval interval)
        {
            CheckInterval every = interval ?? CheckInterval.For(CheckSettings.DefaultSeconds);
            string last = lastAnswerUtc.HasValue
                ? "Last answer " + When(lastAnswerUtc.Value, nowUtc, timeZone)
                : "No answer yet";
            return last + " · " + (every.ChecksByItself ? "tries again " + every.Phrase : "try again when you like");
        }

        /// <summary>The red banner's text when the database doesn't answer.</summary>
        public static string NotReachableDetail(string problem, CheckInterval interval)
        {
            CheckInterval every = interval ?? CheckInterval.For(CheckSettings.DefaultSeconds);
            string reason = string.IsNullOrWhiteSpace(problem) ? "The server doesn't answer." : Sentence(problem);
            return reason + " Is the VPN on? The model keeps its values"
                + (every.ChecksByItself ? ", and the link tries again " + every.Phrase + "." : "; Try again checks once more.");
        }

        /// <summary>The changes list's title, e.g. "3 changes from the database".</summary>
        public static string ChangesTitle(int count) => Count(count, "change", "changes") + " from the database";

        /// <summary>The up-to-date card's line, e.g. "The model matches the database. Last change there: today 14:31 · S. Brandt."</summary>
        public static string UpToDate(ChangeReport report, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            string text = "The model matches the database.";
            if (report == null || !report.NewestChangeUtc.HasValue)
                return text;
            string when = WhenInSentence(report.NewestChangeUtc.Value, nowUtc, timeZone);
            return text + " Last change there: " + when + (report.NewestChangeBy == null ? "" : " · " + report.NewestChangeBy) + ".";
        }

        /// <summary>Who changed a value and when, e.g. "M. Keller · 14:28"; empty when the history doesn't say.</summary>
        public static string Who(ChangeRow row, DateTime nowUtc, TimeZoneInfo timeZone)
        {
            if (row == null)
                return "";
            string when = row.ChangedAtUtc.HasValue ? When(row.ChangedAtUtc.Value, nowUtc, timeZone) : null;
            if (row.ChangedBy == null)
                return when ?? "";
            return when == null ? row.ChangedBy : row.ChangedBy + " · " + when;
        }

        /// <summary>The group's count, e.g. "12 elements", or "material".</summary>
        public static string GroupSize(ChangeGroup group)
        {
            if (group == null)
                return "";
            if (group.IsMaterial)
                return group.ElementIds.Count > 1 ? Count(group.ElementIds.Count, "material", "materials") : "material";
            return Count(group.ElementIds.Count, "element", "elements");
        }

        /// <summary>The three counts' labels under their numbers.</summary>
        public static string LinkedLabel(int count) => count == 1 ? "element linked" : "elements linked";

        public static string WithoutIdLabel => "without Bauteil_ID";

        public static string NotInProjectLabel => "ID not in the project";

        /// <summary>
        /// The note under the changes when linked elements lack some of the database's parameters, e.g.
        /// "Dicke and Ort aren't in this model. Check parameters adds them from the master file."; empty without any.
        /// </summary>
        public static string Missing(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
                return "";
            string which = names.Count <= 3
                ? List(names) + (names.Count == 1 ? " isn't" : " aren't")
                : Count(names.Count, "database parameter", "database parameters") + " aren't";
            return which + " in this model. Check parameters adds them from the master file.";
        }

        /// <summary>The note when some values can't go into their parameters; empty without any.</summary>
        public static string Problems(IReadOnlyList<string> problems)
        {
            if (problems == null || problems.Count == 0)
                return "";
            return "Some values don't fit their parameter in this model: " + string.Join("; ", problems.Take(3))
                + (problems.Count > 3 ? "; and " + Count(problems.Count - 3, "more", "more") : "") + ".";
        }

        /// <summary>The line under the project's name, e.g. "Linked by Y.Abdelazziz, 2026-11-19 15:32. Elements meet it by Bauteil_ID."</summary>
        public static string LinkedLine(string linked) =>
            (string.IsNullOrWhiteSpace(linked) ? "" : linked.Trim() + " ") + "Elements meet it by Bauteil_ID.";

        /// <summary>The note when the values shown are the kept pull, e.g. "Values from the last pull: today 09:12."</summary>
        public static string FromLastPull(DateTime readAtUtc, DateTime nowUtc, TimeZoneInfo timeZone) =>
            "Values from the last pull: " + WhenInSentence(readAtUtc, nowUtc, timeZone) + ".";

        /// <summary>"1 change", "3 changes".</summary>
        public static string Count(int count, string one, string many) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

        // "Dicke", "Dicke and Ort", "Dicke, Ort and Breite".
        private static string List(IReadOnlyList<string> names)
        {
            if (names.Count == 1)
                return names[0];
            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }

        private static string Sentence(string text)
        {
            string trimmed = text.Trim();
            return trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal)
                || trimmed.EndsWith("!", StringComparison.Ordinal)
                ? trimmed
                : trimmed + ".";
        }

        private static DateTime AsUtc(DateTime time) =>
            time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }
}
