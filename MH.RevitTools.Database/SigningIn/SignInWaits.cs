using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.SigningIn
{
    /// <summary>
    /// The waits the database has told a sign-in window about, per user name, so it doesn't offer a try that would be
    /// refused anyway. Names meet as they do in the database: whatever their case.
    /// </summary>
    public sealed class SignInWaits
    {
        private readonly Dictionary<string, DateTime> _until = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public void Remember(string userName, int waitSeconds, DateTime nowUtc)
        {
            string name = (userName ?? "").Trim();
            if (waitSeconds > 0)
                _until[name] = nowUtc.AddSeconds(waitSeconds);
            else
                _until.Remove(name);
        }

        /// <summary>How long this name still has to wait, in whole seconds; 0 for not at all.</summary>
        public int SecondsLeft(string userName, DateTime nowUtc)
        {
            DateTime until;
            if (!_until.TryGetValue((userName ?? "").Trim(), out until))
                return 0;
            double left = (until - nowUtc).TotalSeconds;
            return left <= 0 ? 0 : (int)Math.Ceiling(left);
        }
    }
}
