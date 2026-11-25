using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>What one Apply, or one Create, did, for the pane's banner. Immutable; made by <see cref="ApplyTally"/>.</summary>
    public sealed class ApplyResult
    {
        internal ApplyResult(
            int changes, int elements, int materials, IEnumerable<BorrowedElements> borrowed, int outOfDate, int outdated,
            IEnumerable<string> refused, string stopped, int? created)
        {
            Changes = changes;
            Elements = elements;
            Materials = materials;
            Borrowed = new ReadOnlyCollection<BorrowedElements>((borrowed ?? Enumerable.Empty<BorrowedElements>()).ToList());
            OutOfDate = outOfDate;
            Outdated = outdated;
            Refused = new ReadOnlyCollection<string>((refused ?? Enumerable.Empty<string>()).ToList());
            Stopped = string.IsNullOrWhiteSpace(stopped) ? null : stopped.Trim();
            Created = created;
        }

        /// <summary>How many materials Create made; null for an Apply.</summary>
        public int? Created { get; }

        /// <summary>The model has changed.</summary>
        public bool DidSomething => Changes > 0 || Created > 0;

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

        /// <summary>Listed values, or materials, left out because the database has changed by now.</summary>
        public int Outdated { get; }

        /// <summary>Values, or materials, Revit refused, each with its reason; no more than a few are kept.</summary>
        public IReadOnlyList<string> Refused { get; }

        /// <summary>Why nothing was applied, or created, at all; null when it ran.</summary>
        public string Stopped { get; }

        /// <summary>
        /// True when there was nothing to do and nothing to say, as on a double-click: the first click has
        /// already written everything.
        /// </summary>
        public bool IsEmpty => !DidSomething && Borrowed.Count == 0 && OutOfDate == 0 && Outdated == 0 && Refused.Count == 0 && Stopped == null;

        /// <summary>Nothing went in, for a reason that concerns all of it.</summary>
        public static ApplyResult Stop(string why) => Stop(why, null);

        /// <summary>No material was created, for a reason that concerns all of them.</summary>
        public static ApplyResult StopCreating(string why) => Stop(why, 0);

        private static ApplyResult Stop(string why, int? created) =>
            new ApplyResult(0, 0, 0, null, 0, 0, null, string.IsNullOrWhiteSpace(why) ? "Revit gave no reason." : why, created);
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

    /// <summary>Counts what Apply, or Create, does while it does it.</summary>
    public sealed class ApplyTally
    {
        // Enough to see what's wrong; the pane still lists every value that isn't in.
        private const int MaxRefused = 5;

        private readonly HashSet<string> _changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _elements = new HashSet<long>();
        private readonly HashSet<long> _materials = new HashSet<long>();
        private readonly Dictionary<string, HashSet<long>> _borrowed = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _outOfDate = new HashSet<long>();
        private readonly HashSet<long> _created = new HashSet<long>();
        private readonly List<string> _refused = new List<string>();

        /// <summary>A value went into an element, or a material.</summary>
        /// <param name="key">The object's Bauteil_ID or the material's Material-ID.</param>
        public void Written(string key, bool isMaterial, Guid parameter, long elementId)
        {
            _changes.Add((isMaterial ? "M|" : "O|") + (key ?? "").Trim() + "|" + parameter.ToString("N"));
            (isMaterial ? _materials : _elements).Add(elementId);
        }

        /// <summary>A material got a fill pattern.</summary>
        public void WrittenPattern(string materialId, MaterialPattern which, long elementId)
        {
            _changes.Add("M|" + (materialId ?? "").Trim() + "|" + which);
            _materials.Add(elementId);
        }

        /// <summary>A material was created.</summary>
        public void Created(long elementId) => _created.Add(elementId);

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

        /// <summary>Revit refused a value, or a material.</summary>
        public void Refused(string what, string problem)
        {
            string text = (what ?? "") + ": " + (problem ?? "").Trim().TrimEnd('.');
            if (_refused.Count < MaxRefused && !_refused.Contains(text, StringComparer.Ordinal))
                _refused.Add(text);
        }

        /// <param name="outdated">Listed values the plan left out (<see cref="ApplyPlan.Outdated"/>).</param>
        public ApplyResult Result(int outdated) => new ApplyResult(
            _changes.Count, _elements.Count, _materials.Count,
            _borrowed.OrderBy(owner => owner.Key, StringComparer.OrdinalIgnoreCase)
                .Select(owner => new BorrowedElements(owner.Key.Length == 0 ? null : owner.Key, owner.Value.Count)),
            _outOfDate.Count, outdated, _refused, null, null);

        /// <param name="outdated">Listed materials the plan left out (<see cref="CreationPlan.Outdated"/>).</param>
        public ApplyResult CreationResult(int outdated) => new ApplyResult(0, 0, 0, null, 0, outdated, _refused, null, _created.Count);
    }
}
