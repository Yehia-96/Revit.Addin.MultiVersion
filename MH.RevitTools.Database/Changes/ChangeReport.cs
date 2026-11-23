using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Where the model differs from the database: what the pane lists as "changes from the database", which
    /// is what Apply writes, and what Apply leaves out (<see cref="NotApplied"/>). Made by
    /// <see cref="ChangeFinder"/>. Immutable.
    /// </summary>
    public sealed class ChangeReport
    {
        internal ChangeReport(
            IEnumerable<ChangeGroup> groups,
            ModelCounts counts,
            IEnumerable<NotAppliedItem> notApplied,
            DateTime? newestChangeUtc,
            string newestChangeBy)
        {
            Groups = Freeze(groups);
            Counts = counts ?? throw new ArgumentNullException(nameof(counts));
            NotApplied = Freeze(notApplied);
            NewestChangeUtc = newestChangeUtc;
            NewestChangeBy = string.IsNullOrWhiteSpace(newestChangeBy) ? null : newestChangeBy.Trim();
            ChangeCount = Groups.Sum(group => group.Rows.Count);
        }

        /// <summary>One group per object (Bauteil_ID) or material with changes, the most recently changed first.</summary>
        public IReadOnlyList<ChangeGroup> Groups { get; }

        /// <summary>How many values differ: one per parameter of an object or material, however many elements it has.</summary>
        public int ChangeCount { get; }

        /// <summary>True when there's nothing to apply: every value that can go into the model is in it.</summary>
        public bool IsUpToDate => ChangeCount == 0;

        /// <summary>True when some values differ but can't go into the model now, e.g. as someone has borrowed the elements.</summary>
        public bool HasBlockedDifferences => NotApplied.Any(item => item.Reason == NotAppliedReason.Borrowed
            || item.Reason == NotAppliedReason.OutOfDate || item.Reason == NotAppliedReason.ReadOnly
            || item.Reason == NotAppliedReason.OnType);

        public ModelCounts Counts { get; }

        /// <summary>What Apply leaves out, and why; what someone can act on first.</summary>
        public IReadOnlyList<NotAppliedItem> NotApplied { get; }

        /// <summary>The newest change to any of the project's values in the database, in UTC.</summary>
        public DateTime? NewestChangeUtc { get; }

        /// <summary>Who made <see cref="NewestChangeUtc"/>'s change, as Studio's history names them.</summary>
        public string NewestChangeBy { get; }

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) =>
            new ReadOnlyCollection<T>((items ?? Enumerable.Empty<T>()).ToList());
    }

    /// <summary>The changes on one object (all elements with its Bauteil_ID) or one material. Immutable.</summary>
    public sealed class ChangeGroup
    {
        internal ChangeGroup(string key, string name, bool isMaterial, IEnumerable<long> elementIds, IEnumerable<ChangeRow> rows)
        {
            Key = key ?? "";
            Name = name ?? "";
            IsMaterial = isMaterial;
            ElementIds = new ReadOnlyCollection<long>((elementIds ?? Enumerable.Empty<long>()).ToList());
            Rows = new ReadOnlyCollection<ChangeRow>((rows ?? Enumerable.Empty<ChangeRow>()).ToList());
            NewestChangeUtc = Rows.Where(row => row.ChangedAtUtc.HasValue).Select(row => row.ChangedAtUtc).Max();
        }

        /// <summary>The Bauteil_ID, e.g. BT-1101-01, or the Material-ID, e.g. MB-1761-01-BET-0031.</summary>
        public string Key { get; }

        /// <summary>E.g. "Bodenplatte", "Schlitzrinne links", or the material's name; empty when unknown.</summary>
        public string Name { get; }

        public bool IsMaterial { get; }

        /// <summary>Every element with the Bauteil_ID, or the materials of that name.</summary>
        public IReadOnlyList<long> ElementIds { get; }

        /// <summary>The values that differ, the most recently changed first.</summary>
        public IReadOnlyList<ChangeRow> Rows { get; }

        public DateTime? NewestChangeUtc { get; }
    }

    /// <summary>One parameter's value that differs, on one object or material. Immutable.</summary>
    public sealed class ChangeRow
    {
        internal ChangeRow(
            int parameterId, string parameter, string value, string from, string to, DateTime? changedAtUtc, string changedBy, IEnumerable<long> elementIds)
        {
            ParameterId = parameterId;
            Parameter = parameter ?? "";
            Value = value ?? "";
            From = from ?? "";
            To = to ?? "";
            ChangedAtUtc = changedAtUtc;
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? null : changedBy.Trim();
            ElementIds = new ReadOnlyCollection<long>((elementIds ?? Enumerable.Empty<long>()).ToList());
        }

        public int ParameterId { get; }

        /// <summary>The parameter's Revit name, e.g. "Betonstahl Güte".</summary>
        public string Parameter { get; }

        /// <summary>The database's value as stored, which Apply writes; <see cref="To"/> is how it's shown.</summary>
        public string Value { get; }

        /// <summary>The model's value as Revit shows it, "(empty)", or e.g. "3 different values".</summary>
        public string From { get; }

        /// <summary>The database's value, with its unit, e.g. "B500A" or "0.3 m".</summary>
        public string To { get; }

        /// <summary>When it last changed in the database, in UTC, from Studio's history.</summary>
        public DateTime? ChangedAtUtc { get; }

        /// <summary>Who changed it, as Studio's history names them.</summary>
        public string ChangedBy { get; }

        /// <summary>The elements (or materials) whose value differs.</summary>
        public IReadOnlyList<long> ElementIds { get; }
    }

    /// <summary>
    /// The model's elements of the database's categories, by their Bauteil_ID, for the pane's three counts.
    /// Immutable.
    /// </summary>
    public sealed class ModelCounts
    {
        internal ModelCounts(IEnumerable<long> linked, IEnumerable<long> withoutId, IEnumerable<long> idNotInProject)
        {
            Linked = new ReadOnlyCollection<long>(linked.ToList());
            WithoutId = new ReadOnlyCollection<long>(withoutId.ToList());
            IdNotInProject = new ReadOnlyCollection<long>(idNotInProject.ToList());
        }

        /// <summary>Elements whose Bauteil_ID is one of the project's objects.</summary>
        public IReadOnlyList<long> Linked { get; }

        /// <summary>Elements without a Bauteil_ID.</summary>
        public IReadOnlyList<long> WithoutId { get; }

        /// <summary>Elements whose Bauteil_ID the project doesn't have (or has no values for).</summary>
        public IReadOnlyList<long> IdNotInProject { get; }
    }
}
