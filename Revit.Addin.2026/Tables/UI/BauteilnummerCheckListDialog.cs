using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Revit.Addin._2026.Tables.UI
{
    /// <summary>
    /// Reusable check-list dialog for picking one or more Bauteilnummern from a supplied list.
    /// Used both to (a) add Bauteilnummern from the loaded Excel sheet and (b) exclude
    /// Bauteilnummern that are present in the active view. The caller provides the window
    /// title and the prompt text so the same control serves both purposes.
    /// Selection is returned via <see cref="SelectedBauteilnummern"/>.
    /// </summary>
    public class BauteilnummerCheckListDialog : Form
    {
        public List<string> SelectedBauteilnummern { get; private set; } = new List<string>();

        private readonly CheckedListBox _list;
        private readonly TextBox _search;
        private readonly Label _countLabel;
        private readonly List<string> _allValues;

        public BauteilnummerCheckListDialog(
            string title,
            string prompt,
            IEnumerable<string> bauteilnummern,
            IEnumerable<string> preselected = null)
        {
            _allValues = (bauteilnummern ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, NaturalSortComparer.Instance)
                .ToList();

            Text = string.IsNullOrWhiteSpace(title) ? "Bauteilnummern" : title;
            Width = 480;
            Height = 560;
            MinimumSize = new Size(420, 480);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);

            // Button strip is docked to the bottom of the form (not inside the table layout)
            // so it can never be clipped by an auto-sized row collapsing to zero height.
            var buttons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(12, 8, 12, 8),
            };
            var ok = new Button
            {
                Text = "OK",
                Width = 110,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.OK,
            };
            var cancel = new Button
            {
                Text = "Abbrechen",
                Width = 110,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel,
            };
            // Anchor right, manually position.
            ok.Location = new Point(buttons.ClientSize.Width - ok.Width - 12, 6);
            cancel.Location = new Point(ok.Location.X - cancel.Width - 8, 6);
            buttons.Resize += (s, e) =>
            {
                ok.Location = new Point(buttons.ClientSize.Width - ok.Width - 12, 6);
                cancel.Location = new Point(ok.Location.X - cancel.Width - 8, 6);
            };
            ok.Click += (s, e) =>
            {
                SelectedBauteilnummern = _list.CheckedItems
                    .Cast<object>()
                    .Select(o => o.ToString())
                    .ToList();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // header
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // search
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // toolbar

            layout.Controls.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(prompt)
                    ? "Wählen Sie eine oder mehrere Bauteilnummern:"
                    : prompt,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            }, 0, 0);

            _search = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
            };
            _search.TextChanged += (s, e) => ApplyFilter();
            layout.Controls.Add(_search, 0, 1);

            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
            };
            _list.ItemCheck += (s, e) =>
            {
                // ItemCheck fires before the state flips; defer the count update.
                BeginInvoke((Action)UpdateCountLabel);
            };
            layout.Controls.Add(_list, 0, 2);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 6),
            };

            var btnAll = new Button { Text = "Alle wählen", Width = 110, AutoSize = false };
            var btnNone = new Button { Text = "Auswahl aufheben", Width = 130, AutoSize = false };
            _countLabel = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };
            btnAll.Click += (s, e) => SetAllChecked(true);
            btnNone.Click += (s, e) => SetAllChecked(false);

            toolbar.Controls.Add(btnAll);
            toolbar.Controls.Add(btnNone);
            toolbar.Controls.Add(_countLabel);
            layout.Controls.Add(toolbar, 0, 3);

            AcceptButton = ok;
            CancelButton = cancel;

            // Order matters with docking: add the Bottom-docked button strip first,
            // then the Fill-docked layout so it claims only the remaining space.
            Controls.Add(buttons);
            Controls.Add(layout);

            PopulateList(preselected);
            UpdateCountLabel();
        }

        private void PopulateList(IEnumerable<string> preselected)
        {
            var preset = new HashSet<string>(
                preselected ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var v in _allValues)
                _list.Items.Add(v, preset.Contains(v));
            _list.EndUpdate();
        }

        private void ApplyFilter()
        {
            // Preserve the user's current checked state across the re-filter.
            var checkedNow = new HashSet<string>(
                _list.CheckedItems.Cast<object>().Select(o => o.ToString()),
                StringComparer.OrdinalIgnoreCase);

            string needle = _search.Text?.Trim() ?? string.Empty;

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var v in _allValues)
            {
                if (needle.Length == 0 ||
                    v.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _list.Items.Add(v, checkedNow.Contains(v));
                }
            }
            _list.EndUpdate();
            UpdateCountLabel();
        }

        private void SetAllChecked(bool value)
        {
            for (int i = 0; i < _list.Items.Count; i++)
                _list.SetItemChecked(i, value);
            UpdateCountLabel();
        }

        private void UpdateCountLabel()
        {
            _countLabel.Text = $"{_list.CheckedItems.Count} ausgewählt";
        }

        /// <summary>
        /// Sort "201" before "1001" when both are integers, fall back to ordinal otherwise.
        /// Mirrors the comparer used in TableDataBuilder so the picker order matches the
        /// final table order.
        /// </summary>
        private sealed class NaturalSortComparer : IComparer<string>
        {
            public static readonly NaturalSortComparer Instance = new NaturalSortComparer();

            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                bool xn = long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xv);
                bool yn = long.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yv);

                if (xn && yn) return xv.CompareTo(yv);
                if (xn) return -1;
                if (yn) return 1;
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
