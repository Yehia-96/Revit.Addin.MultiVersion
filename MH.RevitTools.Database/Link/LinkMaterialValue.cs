using System;

namespace MH.RevitTools.Database.Link
{
    /// <summary>One value of one parameter on one material of a project.</summary>
    public sealed class LinkMaterialValue
    {
        public LinkMaterialValue(string materialId, int parameterId, string value, DateTime? changedAtUtc, string changedBy)
        {
            MaterialId = materialId ?? "";
            ParameterId = parameterId;
            Value = value;
            ChangedAtUtc = changedAtUtc;
            ChangedBy = changedBy;
        }

        /// <summary>E.g. MB-1761-01-BET-0031.</summary>
        public string MaterialId { get; }

        public int ParameterId { get; }

        /// <summary>As stored, like <see cref="LinkObjectValue.Value"/>.</summary>
        public string Value { get; }

        /// <summary>When it last changed, in UTC, from Studio's change history; null if there is none.</summary>
        public DateTime? ChangedAtUtc { get; }

        /// <summary>Who changed it last, as Studio's history names them; null if there is none.</summary>
        public string ChangedBy { get; }
    }
}
