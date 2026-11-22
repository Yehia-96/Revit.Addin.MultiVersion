using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// How often the MH Database pane looks for changes: each person's own choice, kept on their PC in
    /// <c>%APPDATA%\MH\RevitTools\settings.json</c>, never in the connection file, which is the same for
    /// everyone on the PC. Immutable; <see cref="TrySave"/> writes it back.
    /// </summary>
    public sealed class CheckSettings
    {
        /// <summary>Every minute, as the mockup has it.</summary>
        public const int DefaultSeconds = 60;

        private const string IntervalKey = "checkEverySeconds";

        // Settings are a few lines; anything far bigger isn't ours, and reading it would only stall Revit.
        private const long MaxFileBytes = 64 * 1024;

        public CheckSettings(int checkEverySeconds)
        {
            Interval = CheckInterval.For(checkEverySeconds) ?? CheckInterval.For(DefaultSeconds);
        }

        /// <summary>Where each person's settings live: <c>%APPDATA%\MH\RevitTools\settings.json</c>.</summary>
        public static string DefaultPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MH", "RevitTools", "settings.json");

        public CheckInterval Interval { get; }

        /// <summary>
        /// The settings in the file; the default (every minute) when there's no file, or it can't be read or
        /// holds an interval the pane doesn't offer.
        /// </summary>
        public static CheckSettings Load(string path)
        {
            JObject root = ReadObject(path);
            JToken interval = root == null ? null : root[IntervalKey];
            int seconds;
            if (interval == null || interval.Type != JTokenType.Integer || !int.TryParse(interval.ToString(), out seconds))
                seconds = DefaultSeconds;
            return new CheckSettings(seconds);
        }

        /// <summary>
        /// Writes the interval into the file, keeping anything else it holds. False, with the reason, when
        /// the file can't be written; the pane keeps the choice for this session anyway.
        /// </summary>
        public bool TrySave(string path, out string problem)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A path is required.", nameof(path));

            JObject root = ReadObject(path) ?? new JObject();
            root[IntervalKey] = Interval.Seconds;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SafeFile.WriteAllText(path, root.ToString(Formatting.Indented));
                problem = null;
                return true;
            }
            catch (IOException ex)
            {
                problem = "Couldn't save your settings in " + path + ": " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                problem = "You don't have permission to save your settings in " + path + ".";
                return false;
            }
        }

        private static JObject ReadObject(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length > MaxFileBytes)
                    return null;
                return JToken.Parse(File.ReadAllText(path, Encoding.UTF8)) as JObject;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>One of the pane's "Look for changes" choices. Immutable.</summary>
    public sealed class CheckInterval
    {
        private CheckInterval(int seconds, string label, string phrase)
        {
            Seconds = seconds;
            Label = label;
            Phrase = phrase;
        }

        /// <summary>The choices the pane offers, in its order.</summary>
        public static IReadOnlyList<CheckInterval> All { get; } = new ReadOnlyCollection<CheckInterval>(new[]
        {
            new CheckInterval(30, "Every 30 seconds", "every 30 seconds"),
            new CheckInterval(60, "Every minute", "every minute"),
            new CheckInterval(300, "Every 5 minutes", "every 5 minutes"),
            new CheckInterval(0, "Only when I ask", "only when you ask"),
        });

        /// <summary>Seconds between checks; 0 for "only when I ask".</summary>
        public int Seconds { get; }

        /// <summary>As the pane's list shows it, e.g. "Every minute".</summary>
        public string Label { get; }

        /// <summary>For inside a sentence, e.g. "every minute", "only when you ask".</summary>
        public string Phrase { get; }

        /// <summary>False for "only when I ask".</summary>
        public bool ChecksByItself => Seconds > 0;

        /// <summary>The choice with that many seconds; null when the pane doesn't offer it.</summary>
        public static CheckInterval For(int seconds) => All.FirstOrDefault(choice => choice.Seconds == seconds);

        public override string ToString() => Label;
    }
}
