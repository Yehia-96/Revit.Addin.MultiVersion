using MH.RevitTools.Database.Parameters;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Lists the elements Revit deletes together with the parameters Replace would delete, by name, kind
    /// and id, and asks before anything goes (Yehia, 2026-11-22): delete them and replace, replace only the
    /// parameters that go on their own, or cancel. It only asks; Replace does the deleting.
    /// </summary>
    public partial class AlsoDeletedWindow : Window
    {
        // More rows than anyone reads, few enough to draw at once.
        private const int RowsShown = 500;

        public AlsoDeletedWindow(ReplaceDeletions deletions)
        {
            if (deletions == null) throw new ArgumentNullException(nameof(deletions));

            InitializeComponent();
            TitleText.Text = ParameterCheckTexts.DeletionTitle(deletions);
            IntroText.Text = ParameterCheckTexts.DeletionIntro(deletions);
            DeleteButton.Content = ParameterCheckTexts.DeleteAndReplaceButton(deletions);

            string others = ParameterCheckTexts.DeletionOthersButton(deletions);
            OthersButton.Content = others;
            OthersButton.Visibility = others.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            // Each element once, grouped by the parameter it goes with; one that several take names them all.
            Rows.ItemsSource = deletions.Elements
                .Take(RowsShown)
                .Select(element => new DeletedElementRow(element, deletions.ParametersOf(element)))
                .ToList();
            int hidden = deletions.Elements.Count - RowsShown;
            if (hidden > 0)
            {
                MoreText.Text = "And " + hidden.ToString(CultureInfo.InvariantCulture) + " more.";
                MoreText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>The answer; Cancel unless a button said otherwise.</summary>
        public ReplaceChoice Choice { get; private set; } = ReplaceChoice.Cancel;

        /// <summary>Shows the list over the owner, and gives the answer once it's closed.</summary>
        public static ReplaceChoice Ask(Window owner, ReplaceDeletions deletions)
        {
            var window = new AlsoDeletedWindow(deletions) { Owner = owner };
            window.ShowDialog();
            return window.Choice;
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            Choice = ReplaceChoice.ReplaceAll;
            DialogResult = true;
        }

        private void OnOthers(object sender, RoutedEventArgs e)
        {
            Choice = ReplaceChoice.ReplaceOthers;
            DialogResult = true;
        }

        // Keeps the column headings over their columns while the list shows a scroll bar.
        private void OnRowsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double bar = RowsScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? SystemParameters.VerticalScrollBarWidth
                : 0;
            HeaderColumns.Margin = new Thickness(0, 0, bar, 0);
        }
    }
}
