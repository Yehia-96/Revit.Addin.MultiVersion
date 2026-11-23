using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>What one Apply did, for the pane's banner. Immutable; made by <see cref="ApplyTally"/>.</summary>
    public sealed class ApplyResult
    {
        internal ApplyResult(
            int changes, int elements, int materials, IEnumerable<BorrowedElements> borrowed, int outOfDate, int outdated,
            IEnumerable<string> refused, string stopped)
        {
            Changes = changes;
            Elements = elements;
            Materials = materials;
            Borrowed = new ReadOnlyCollection<BorrowedElements>((borrowed ?? Enumerable.Empty<BorrowedElements>()).ToList());
            OutOfDate = outOfDate;
            Outdated = outdated;
            Refused = new ReadOnlyCollection<string>((refused ?? Enumerable.Empty<string>()).ToList());
            Stopped = string.IsNullOrWhiteSpace(stopped) ? null : stopped.Trim();
        }

        /// <summary>How many changes went in: one per parameter of an object or material, as the pane counts them.</summary>
        public int Changes { get; }

        /// <summary>How many elements got values.</summary>
        public int Elements { get; }

        /// <summary>How many materials got values.</summary>
        public int Materials { get; }

        /// <summary>The elements left out because someone else has them, per person.</summary>
        public IReadOnlyList<BorrowedElements> Borrowed { get; }

        /// <summary>The elements left out because the central model has newer changes to them.</summary>
        public int OutOfDate { get; }

        /// <summary>Listed values left out because the database has another value by now.</summary>
        public int Outdated { get; }

        /// <summary>Values Revit refused, each with its reason; no more than a few are kept.</summary>
        public IReadOnlyList<string> Refused { get; }

        /// <summary>Why nothing was applied at all; null when Apply ran.</summary>
        public string Stopped { get; }

        /// <summary>
        /// True when there was nothing to do and nothing to say, as on a double-click: the first click has
        /// already written everything.
        /// </summary>
        public bool IsEmpty => Changes == 0 && Borrowed.Count == 0 && OutOfDate == 0 && Outdated == 0 && Refused.Count == 0 && Stopped == null;

        /// <summary>Nothing went in, for a reason that concerns all of it.</summary>
        public static ApplyResult Stop(string why) =>
            new ApplyResult(0, 0, 0, null, 0, 0, null, string.IsNullOrWhiteSpace(why) ? "Revit gave no reason." : why);
    }

    /// <summary>Elements someone else has borrowed. Immutable.</summary>
    public sealed class BorrowedElements
    {
        internal BorrowedElements(string owner, int count)
        {
            Owner = owner;
            Count = count;
        }

        /// <summary>Who has them; null when Revit doesn't say.</summary>
        public string Owner { get; }

        public int Count { get; }
    }

    /// <summary>Counts what Apply does while it does it.</summary>
    public sealed class ApplyTally
    {
        // Enough to see what's wrong; the pane still lists every value that isn't in.
        private const int MaxRefused = 5;

        private readonly HashSet<string> _changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _elements = new HashSet<long>();
        private readonly HashSet<long> _materials = new HashSet<long>();
        private readonly Dictionary<string, HashSet<long>> _borrowed = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _outOfDate = new HashSet<long>();
        private readonly List<string> _refused = new List<string>();

        /// <summary>A value went into an element, or a material.</summary>
        /// <param name="key">The object's Bauteil_ID or the material's Material-ID.</param>
        public void Written(string key, bool isMaterial, Guid parameter, long elementId)
        {
            _changes.Add((isMaterial ? "M|" : "O|") + (key ?? "").Trim() + "|" + parameter.ToString("N"));
            (isMaterial ? _materials : _elements).Add(elementId);
        }

        public void Borrowed(string owner, long elementId)
        {
            string name = string.IsNullOrWhiteSpace(owner) ? "" : owner.Trim();
            HashSet<long> ids;
            if (!_borrowed.TryGetValue(name, out ids))
            {
                ids = new HashSet<long>();
                _borrowed.Add(name, ids);
            }
            ids.Add(elementId);
        }

        public void OutOfDate(long elementId) => _outOfDate.Add(elementId);

        /// <summary>Revit refused a value.</summary>
        public void Refused(string parameter, string problem)
        {
            string text = (parameter ?? "") + ": " + (problem ?? "").Trim().TrimEnd('.');
            if (_refused.Count < MaxRefused && !_refused.Contains(text, StringComparer.Ordinal))
                _refused.Add(text);
        }

        /// <param name="outdated">Listed values the plan left out (<see cref="ApplyPlan.Outdated"/>).</param>
        public ApplyResult Result(int outdated) => new ApplyResult(
            _changes.Count, _elements.Count, _materials.Count,
            _borrowed.OrderBy(owner => owner.Key, StringComparer.OrdinalIgnoreCase)
                .Select(owner => new BorrowedElements(owner.Key.Length == 0 ? null : owner.Key, owner.Value.Count)),
            _outOfDate.Count, outdated, _refused, null);
    }
}
