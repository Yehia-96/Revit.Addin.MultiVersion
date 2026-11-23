using MH.RevitTools.Database.Comparing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace Revit.Addin._2027.DatabaseLink.Compare
{
    /// <summary>Shows what <see cref="LoadComparison"/> found. Temporary (step 7).</summary>
    public partial class CompareWindow : Window
    {
        private readonly string _text;

        /// <param name="sources">What was compared, e.g. "Planungsdatenbank.xlsx and 1761-01 Ulm".</param>
        public CompareWindow(LoadComparison comparison, string sources)
        {
            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));

            InitializeComponent();
            _text = sources + Environment.NewLine + comparison.ToText();
            DataContext = new View(comparison, sources);
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_text);
                FooterText.Text = "Copied. Paste it anywhere.";
            }
            catch (ExternalException ex)
            {
                // Another program holds the clipboard at the moment.
                FooterText.Text = "Windows didn't let it be copied: " + ex.Message;
            }
        }

        /// <summary>What the window binds to.</summary>
        public sealed class View
        {
            public View(LoadComparison comparison, string sources)
            {
                Headline = comparison.Title;
                Sources = sources ?? "";
                Summary = comparison.Summary;
                Sections = comparison.Sections.Select(section => new SectionView(section)).ToList();
                bool identical = comparison.IsIdentical;
                Icon = identical ? "M20,6 L9,17 L4,12" : "M12,4 V14 M12,19 V19.01";
                IconColor = identical ? "#1F8A4C" : "#B7791F";
                IconBack = identical ? "#E7F5EC" : "#FDF3E1";
            }

            public string Headline { get; }
            public string Sources { get; }
            public string Summary { get; }
            public string Icon { get; }
            public string IconColor { get; }
            public string IconBack { get; }
            public IReadOnlyList<SectionView> Sections { get; }
        }

        /// <summary>One section of the comparison.</summary>
        public sealed class SectionView
        {
            // Enough to work with; Copy the list gives them all.
            private const int MaxLines = 400;

            public SectionView(ComparisonSection section)
            {
                bool known = section.Kind == ComparisonSectionKind.ByDesign;
                int more = section.Lines.Count - MaxLines;
                Title = section.Title;
                Count = section.Count.ToString(CultureInfo.InvariantCulture);
                CountColor = known ? "#5B6578" : "#8F5B0A";
                CountBack = known ? "#ECEEF2" : "#FDF3E1";
                Text = string.Join(Environment.NewLine, section.Lines.Take(MaxLines))
                    + (more > 0 ? Environment.NewLine + "… and " + more.ToString(CultureInfo.InvariantCulture) + " more: use Copy the list." : "");
            }

            public string Title { get; }
            public string Count { get; }
            public string CountColor { get; }
            public string CountBack { get; }
            public string Text { get; }
        }
    }
}
