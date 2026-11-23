using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>Whether an element can be changed, as far as other people's work goes.</summary>
    internal enum ElementAccess
    {
        Free,

        /// <summary>Someone else has borrowed it.</summary>
        Borrowed,

        /// <summary>The central model has newer changes to it, or it's deleted there.</summary>
        OutOfDate,
    }

    /// <summary>
    /// What other people are doing to a workshared model's elements. <see cref="AccessTo"/> asks Revit's local
    /// copy of it, which is quick but can be behind; it's only for showing. <see cref="Borrow"/> asks the
    /// central model, right before something is written. API context; one instance per comparison or apply.
    /// </summary>
    internal sealed class Worksharing
    {
        private readonly Document _document;
        private readonly Dictionary<long, Tuple<ElementAccess, string>> _known = new Dictionary<long, Tuple<ElementAccess, string>>();

        public Worksharing(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            // A detached model has no central model to ask, and everything in it is the user's own.
            IsShared = document.IsWorkshared && !document.IsDetached;
        }

        public bool IsShared { get; }

        /// <summary>As Revit's local copy has it; <paramref name="owner"/> is who borrowed it.</summary>
        public ElementAccess AccessTo(ElementId id, out string owner)
        {
            owner = null;
            if (!IsShared)
                return ElementAccess.Free;

            Tuple<ElementAccess, string> known;
            if (!_known.TryGetValue(id.Value, out known))
            {
                known = Ask(id);
                _known.Add(id.Value, known);
            }
            owner = known.Item2;
            return known.Item1;
        }

        /// <summary>
        /// Borrows the elements from the central model, all in one go, and says for each whether it can be
        /// changed now. Throws what Revit throws when the central model can't be reached. Call it outside a
        /// transaction.
        /// </summary>
        /// <param name="blocked">Called for every element that can't be changed, with who has it.</param>
        /// <returns>The elements that can be changed.</returns>
        public HashSet<long> Borrow(ICollection<ElementId> ids, Action<ElementId, ElementAccess, string> blocked)
        {
            var free = new HashSet<long>();
            if (!IsShared)
            {
                foreach (ElementId id in ids)
                    free.Add(id.Value);
                return free;
            }

            var owned = new HashSet<long>();
            foreach (ElementId id in WorksharingUtils.CheckoutElements(_document, ids))
                owned.Add(id.Value);
            foreach (ElementId id in ids)
            {
                Tuple<ElementAccess, string> now = Ask(id);
                if (!owned.Contains(id.Value))
                    blocked(id, now.Item1 == ElementAccess.OutOfDate ? ElementAccess.OutOfDate : ElementAccess.Borrowed, now.Item2);
                else if (now.Item1 == ElementAccess.OutOfDate)
                    blocked(id, ElementAccess.OutOfDate, null);
                else
                    free.Add(id.Value);
            }
            return free;
        }

        private Tuple<ElementAccess, string> Ask(ElementId id)
        {
            string owner;
            if (WorksharingUtils.GetCheckoutStatus(_document, id, out owner) == CheckoutStatus.OwnedByOtherUser)
                return Tuple.Create(ElementAccess.Borrowed, owner);
            ModelUpdatesStatus updates = WorksharingUtils.GetModelUpdatesStatus(_document, id);
            return updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral
                ? Tuple.Create(ElementAccess.OutOfDate, (string)null)
                : Tuple.Create(ElementAccess.Free, (string)null);
        }
    }
}
