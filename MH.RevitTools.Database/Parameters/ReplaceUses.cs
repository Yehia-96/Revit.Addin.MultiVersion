using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// Which of the model's parameters Replace would delete are used in schedules or view filters. Deleting
    /// a parameter takes it out of them, so Replace asks once more before it swaps those (Yehia,
    /// 2026-11-22). When Revit couldn't tell, each one counts as used. Immutable.
    /// </summary>
    public sealed class ReplaceUses
    {
        private readonly IReadOnlyList<ParameterUse> _uses;

        private ReplaceUses(IReadOnlyList<ParameterToReplace> toReplace, IReadOnlyList<ParameterUse> uses, string problem)
        {
            _uses = uses;
            Problem = problem;
            All = Freeze(toReplace);
            Used = Freeze(toReplace.Where(item => problem != null || IsUsed(item.Old)));
            Others = Freeze(toReplace.Where(item => problem == null && !IsUsed(item.Old)));
        }

        /// <summary>What the add-in found in the model.</summary>
        public static ReplaceUses Found(IReadOnlyList<ParameterToReplace> toReplace, IEnumerable<ParameterUse> uses)
        {
            if (toReplace == null) throw new ArgumentNullException(nameof(toReplace));
            if (uses == null) throw new ArgumentNullException(nameof(uses));
            return new ReplaceUses(toReplace, Freeze(uses), null);
        }

        /// <summary>Revit couldn't tell where they're used, for this reason: every one counts as used.</summary>
        public static ReplaceUses Unknown(IReadOnlyList<ParameterToReplace> toReplace, string problem)
        {
            if (toReplace == null) throw new ArgumentNullException(nameof(toReplace));
            return new ReplaceUses(toReplace, Freeze(Enumerable.Empty<ParameterUse>()),
                string.IsNullOrWhiteSpace(problem) ? "no reason given" : problem.Trim());
        }

        /// <summary>Every swap Replace would do.</summary>
        public IReadOnlyList<ParameterToReplace> All { get; }

        /// <summary>The swaps whose model parameter schedules or view filters use.</summary>
        public IReadOnlyList<ParameterToReplace> Used { get; }

        /// <summary>The swaps whose model parameter nothing else uses.</summary>
        public IReadOnlyList<ParameterToReplace> Others { get; }

        /// <summary>Why Revit couldn't tell where they're used; null when it could.</summary>
        public string Problem { get; }

        /// <summary>The model's parameters in use, each once: two swaps may delete the same one.</summary>
        public IReadOnlyList<ModelBinding> UsedParameters => Distinct(Used);

        /// <summary>The model's parameters nothing else uses, each once.</summary>
        public IReadOnlyList<ModelBinding> OtherParameters => Distinct(Others);

        /// <summary>The schedules and view filters that use the model's parameter, in the order found.</summary>
        public IReadOnlyList<ParameterUse> UsesOf(ModelBinding parameter)
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            return Freeze(_uses.Where(use => use.ParameterElementId == parameter.ElementId));
        }

        /// <summary>Whether schedules use any of them, and whether view filters do.</summary>
        public bool AnyUse(ParameterUseKind kind) => _uses.Any(use => use.Kind == kind);

        /// <summary>What to replace after the answer; empty for Cancel.</summary>
        public IReadOnlyList<ParameterToReplace> Choose(ReplaceChoice choice)
        {
            switch (choice)
            {
                case ReplaceChoice.ReplaceAll: return All;
                case ReplaceChoice.ReplaceOthers: return Others;
                default: return Freeze(Enumerable.Empty<ParameterToReplace>());
            }
        }

        private bool IsUsed(ModelBinding parameter) =>
            parameter.ElementId != 0 && _uses.Any(use => use.ParameterElementId == parameter.ElementId);

        private static IReadOnlyList<ModelBinding> Distinct(IEnumerable<ParameterToReplace> swaps) => Swaps.OldParameters(swaps);

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Swaps.Freeze(items);
    }

    /// <summary>What Replace's questions share about a list of swaps.</summary>
    internal static class Swaps
    {
        /// <summary>The model's parameters the swaps delete, each once: two swaps may delete the same one.</summary>
        public static IReadOnlyList<ModelBinding> OldParameters(IEnumerable<ParameterToReplace> swaps)
        {
            var seen = new HashSet<long>();
            var parameters = new List<ModelBinding>();
            foreach (ParameterToReplace swap in swaps)
            {
                // An unknown id (0) can't be matched, so such a parameter counts on its own.
                if (swap.Old.ElementId == 0 || seen.Add(swap.Old.ElementId))
                    parameters.Add(swap.Old);
            }
            return parameters.AsReadOnly();
        }

        public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => new ReadOnlyCollection<T>(items.ToList());
    }
}
