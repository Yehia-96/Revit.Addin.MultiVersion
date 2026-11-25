using System;

namespace MH.RevitTools.Database.Link
{
    /// <summary>
    /// What one object (Bauteil) of a project is made of. Only objects that have a material are listed; it can be one of
    /// the project's own, a global one, or one that was moved to another project in Studio.
    /// </summary>
    public sealed class LinkObjectMaterial
    {
        public LinkObjectMaterial(string bauteilId, string materialId, string materialName, DateTime? changedAtUtc, string changedBy)
        {
            BauteilId = bauteilId ?? "";
            MaterialId = string.IsNullOrWhiteSpace(materialId) ? null : materialId.Trim();
            MaterialName = materialName ?? "";
            ChangedAtUtc = changedAtUtc;
            ChangedBy = changedBy;
        }

        /// <summary>E.g. BT-1101-01, as in <see cref="LinkObjectValue.BauteilId"/>. Two objects can share one.</summary>
        public string BauteilId { get; }

        /// <summary>E.g. MB-1761-01-BET-0013, or MP-9999-99-BET-0101; null for a material without a full ID.</summary>
        public string MaterialId { get; }

        /// <summary>The Materialname, by which Revit knows it, e.g. "13-Beton, C20/25".</summary>
        public string MaterialName { get; }

        /// <summary>When it was set last, in UTC, from Studio's change history; null if there is none.</summary>
        public DateTime? ChangedAtUtc { get; }

        /// <summary>Who set it last, or approved it, as Studio's history names them; null if there is none.</summary>
        public string ChangedBy { get; }
    }
}
