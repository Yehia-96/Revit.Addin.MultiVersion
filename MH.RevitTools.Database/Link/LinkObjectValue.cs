using System;

namespace MH.RevitTools.Database.Link
{
    /// <summary>One value of one parameter on one object (Bauteil) of a project.</summary>
    public sealed class LinkObjectValue
    {
        public LinkObjectValue(string bauteilId, int parameterId, string value, DateTime? changedAtUtc, string changedBy)
        {
            BauteilId = bauteilId ?? "";
            ParameterId = parameterId;
            Value = value;
            ChangedAtUtc = changedAtUtc;
            ChangedBy = changedBy;
        }

        /// <summary>
        /// E.g. BT-1101-01 or ET-2101-01: the key the modellers enter on every element in Revit. Built
        /// from the object's catalog entry the way Studio builds it.
        /// </summary>
        public string BauteilId { get; }

        public int ParameterId { get; }

        /// <summary>
        /// As stored: numbers in the invariant format in the parameter's unit, placeholders such as
        /// [Modeller] unchanged. May be null or empty.
        /// </summary>
        public string Value { get; }

        /// <summary>When it last changed, in UTC, from Studio's change history; null if there is none.</summary>
        public DateTime? ChangedAtUtc { get; }

        /// <summary>Who changed it last, as Studio's history names them; null if there is none.</summary>
        public string ChangedBy { get; }
    }
}
