using System;
using System.Collections.Generic;
using System.Linq;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// What else Revit deletes together with the model's parameters Replace would delete. Revit can't
    /// delete some parameters on their own, so Replace lists those elements by name and deletes them only
    /// once the user agrees (Yehia, 2026-11-22); it never deletes one it didn't list. Immutable.
    /// </summary>
    public sealed class ReplaceDeletions
    {
        // Every parameter-and-element pair the trial found, the parameters being replaced left out. An element
        // two parameters take is in here twice, so each of them counts as deleting it.
        private readonly IReadOnlyList<DeletedElement> _found;

        private ReplaceDeletions(IReadOnlyList<ParameterToReplace> toReplace, IReadOnlyList<DeletedElement> found)
        {
            _found = found;
            All = Swaps.Freeze(toReplace);
            Deleting = Swaps.Freeze(toReplace.Where(swap => TakesElements(swap.Old)));
            Others = Swaps.Freeze(toReplace.Where(swap => !TakesElements(swap.Old)));
            DeletingParameters = Swaps.OldParameters(Deleting);
            OtherParameters = Swaps.OldParameters(Others);
            Elements = EachOnce(toReplace, found);
        }

        /// <param name="toReplace">The swaps Replace would do.</param>
        /// <param name="elements">What Revit deleted with each model parameter when the add-in tried.</param>
        public static ReplaceDeletions Found(IReadOnlyList<ParameterToReplace> toReplace, IEnumerable<DeletedElement> elements)
        {
            if (toReplace == null) throw new ArgumentNullException(nameof(toReplace));
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            var swapped = new HashSet<long>(toReplace.Select(swap => swap.Old.ElementId));
            return new ReplaceDeletions(toReplace, Swaps.Freeze(elements.Where(element => !swapped.Contains(element.Id))));
        }

        /// <summary>Every swap Replace would do.</summary>
        public IReadOnlyList<ParameterToReplace> All { get; }

        /// <summary>The swaps whose model parameter Revit deletes other elements with.</summary>
        public IReadOnlyList<ParameterToReplace> Deleting { get; }

        /// <summary>The swaps whose model parameter goes on its own.</summary>
        public IReadOnlyList<ParameterToReplace> Others { get; }

        /// <summary>
        /// Every element Revit deletes besides the parameters, each once, grouped by the first parameter
        /// that takes it.
        /// </summary>
        public IReadOnlyList<DeletedElement> Elements { get; }

        /// <summary>The model's parameters Revit deletes other elements with, each once.</summary>
        public IReadOnlyList<ModelBinding> DeletingParameters { get; }

        /// <summary>The model's parameters that go on their own, each once.</summary>
        public IReadOnlyList<ModelBinding> OtherParameters { get; }

        /// <summary>The names of the model's parameters Revit deletes the element with, e.g. "Dicke, Breite".</summary>
        public string ParametersOf(DeletedElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            var takers = new HashSet<long>(_found.Where(pair => pair.Id == element.Id).Select(pair => pair.ParameterElementId));
            return string.Join(", ", DeletingParameters.Where(parameter => takers.Contains(parameter.ElementId)).Select(parameter => parameter.Name));
        }

        /// <summary>What to replace after the answer; empty for Cancel.</summary>
        public IReadOnlyList<ParameterToReplace> Choose(ReplaceChoice choice)
        {
            switch (choice)
            {
                case ReplaceChoice.ReplaceAll: return All;
                case ReplaceChoice.ReplaceOthers: return Others;
                default: return Swaps.Freeze(Enumerable.Empty<ParameterToReplace>());
            }
        }

        /// <summary>The element ids Replace may delete besides the parameters after the answer: the listed ones, or none.</summary>
        public IReadOnlyCollection<long> AllowedFor(ReplaceChoice choice) =>
            choice == ReplaceChoice.ReplaceAll
                ? Swaps.Freeze(Elements.Select(element => element.Id))
                : Swaps.Freeze(Enumerable.Empty<long>());

        private bool TakesElements(ModelBinding parameter) =>
            parameter.ElementId != 0 && _found.Any(pair => pair.ParameterElementId == parameter.ElementId);

        // Each element once, as first found, ordered by its parameter's place among the swaps (a stable sort).
        private static IReadOnlyList<DeletedElement> EachOnce(IReadOnlyList<ParameterToReplace> toReplace, IReadOnlyList<DeletedElement> found)
        {
            var place = new Dictionary<long, int>();
            for (int i = 0; i < toReplace.Count; i++)
            {
                if (!place.ContainsKey(toReplace[i].Old.ElementId))
                    place.Add(toReplace[i].Old.ElementId, i);
            }
            var seen = new HashSet<long>();
            return Swaps.Freeze(found
                .Where(pair => seen.Add(pair.Id))
                .OrderBy(pair => place.TryGetValue(pair.ParameterElementId, out int at) ? at : int.MaxValue));
        }
    }
}
