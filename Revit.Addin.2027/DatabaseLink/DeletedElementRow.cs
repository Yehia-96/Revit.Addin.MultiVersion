using MH.RevitTools.Database.Parameters;
using System;
using System.Globalization;
using System.Windows;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>One line of the list of elements Replace would delete, ready to bind.</summary>
    public sealed class DeletedElementRow
    {
        public DeletedElementRow(DeletedElement element, string parameter)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            Name = element.Name;
            Note = element.Note;
            Kind = element.Kind;
            Id = element.Id.ToString(CultureInfo.InvariantCulture);
            Parameter = parameter ?? "";
        }

        public string Name { get; }

        /// <summary>Where it is, under its name, e.g. "In schedule “Wandliste”".</summary>
        public string Note { get; }

        public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;

        public string Kind { get; }

        /// <summary>For Manage › Select by ID.</summary>
        public string Id { get; }

        /// <summary>The model's parameter it goes with.</summary>
        public string Parameter { get; }
    }
}
