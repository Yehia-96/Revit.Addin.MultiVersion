using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Revit.Addin._2026.Services;
using Revit.Addin._2026.Tables.Exporters;
using Revit.Addin._2026.Tables.Templates;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Revit.Addin._2026.Tables.UI
{
    /// <summary>
    /// Lets the user pick (a) a fixed-format table template and (b) an output format.
    /// Templates and exporters are read from their respective registries, so this UI
    /// automatically picks up new ones without code changes here.
    ///
    /// Optionally the user can also:
    ///   • mix in Bauteilnummern from the loaded Excel sheet
    ///     (<see cref="ExcelParser.FamilyData"/>) so elements that are never modelled can
    ///     still appear in the final table; and
    ///   • exclude Bauteilnummern that are present in the active view so they are left out
    ///     of the generated table.
    /// </summary>
    public class TableExportDialog : Form
    {
        public ITableTemplate SelectedTemplate { get; private set; }
        public ITableExporter SelectedExporter { get; private set; }

        /// <summary>
        /// Bauteilnummern (as strings) the user picked from the loaded Excel sheet to
        /// include in the table in addition to the elements found in the active view.
        /// Empty when the user did not use the Excel picker.
        /// </summary>
        public IReadOnlyList<string> SelectedExcelBauteilnummern { get; private set; } =
            new List<string>();

        /// <summary>
        /// Bauteilnummern present in the active view that the user chose to OMIT from the
        /// table. Empty when the exclusion checkbox is off.
        /// </summary>
        public IReadOnlyList<string> SelectedExcludedBauteilnummern { get; private set; } =
            new List<string>();

        private readonly ComboBox _templateCombo;
        private readonly ComboBox _formatCombo;
        private readonly Label _excelCountLabel;
        private readonly CheckBox _excludeCheck;
        private readonly Label _excludeCountLabel;

        private readonly List<string> _excelSelection = new List<string>();
        private readonly List<string> _excludeSelection = new List<string>();

        // Supplies the in-view Bauteilnummern for a given template (injected by the command
        // so this UI stays free of Revit document access). May be null in design scenarios.
        private readonly Func<ITableTemplate, IReadOnlyList<string>> _inViewProvider;

        // Guards against re-entrancy when we programmatically reset the exclusion checkbox.
        private bool _suppressExcludeEvent;

        public TableExportDialog(IReadOnlyList<ITableTemplate> templates,
                                 IReadOnlyList<ITableExporter> exporters,
                                 Func<ITableTemplate, IReadOnlyList<string>> inViewBauteilnummernProvider = null)
        {
            _inViewProvider = inViewBauteilnummernProvider;

            Text = "Tabelle erstellen";
            Width = 580;
            Height = 330;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 6,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // template
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // format
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // excel row
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // exclude row
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // spacer
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

            layout.Controls.Add(MakeLabel("Tabellenvorlage:"), 0, 0);
            _templateCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Title",
            };
            foreach (var t in templates) _templateCombo.Items.Add(new TemplateItem(t));
            if (_templateCombo.Items.Count > 0) _templateCombo.SelectedIndex = 0;
            // Changing the template invalidates an exclusion list built for the previous one.
            _templateCombo.SelectedIndexChanged += (s, e) => ResetExcludeCheck();
            layout.Controls.Add(_templateCombo, 1, 0);

            layout.Controls.Add(MakeLabel("Ausgabeformat:"), 0, 1);
            _formatCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            foreach (var e in exporters) _formatCombo.Items.Add(new ExporterItem(e));
            if (_formatCombo.Items.Count > 0) _formatCombo.SelectedIndex = 0;
            layout.Controls.Add(_formatCombo, 1, 1);

            // Excel row: button + selection-count label.
            layout.Controls.Add(MakeLabel("Aus Excel:"), 0, 2);

            var excelRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                AutoSize = true,
            };
            excelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            excelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var excelPickerButton = new Button
            {
                Text = "Aus Excel hinzufügen…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            excelPickerButton.Click += (s, e) => OpenExcelPicker();
            excelRow.Controls.Add(excelPickerButton, 0, 0);

            _excelCountLabel = new Label
            {
                Text = "Keine zusätzlichen Bauteilnummern ausgewählt.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 0, 0),
            };
            excelRow.Controls.Add(_excelCountLabel, 1, 0);
            layout.Controls.Add(excelRow, 1, 2);

            // Exclude row: checkbox (opens the in-view picker) + selection-count label.
            layout.Controls.Add(MakeLabel("Ausschließen:"), 0, 3);

            var excludeRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                AutoSize = true,
            };
            excludeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            excludeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _excludeCheck = new CheckBox
            {
                Text = "Bauteilnummern wählen…",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
            };
            _excludeCheck.CheckedChanged += ExcludeCheck_CheckedChanged;
            excludeRow.Controls.Add(_excludeCheck, 0, 0);

            _excludeCountLabel = new Label
            {
                Text = "Keine Bauteilnummern ausgeschlossen.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 0, 0),
            };
            excludeRow.Controls.Add(_excludeCountLabel, 1, 0);
            layout.Controls.Add(excludeRow, 1, 3);

            // Spacer row.
            layout.Controls.Add(new Label { Dock = DockStyle.Fill }, 0, 4);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
            };
            var ok = new Button { Text = "Erstellen", Width = 110, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Abbrechen", Width = 100, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                SelectedTemplate = (_templateCombo.SelectedItem as TemplateItem)?.Template;
                SelectedExporter = (_formatCombo.SelectedItem as ExporterItem)?.Exporter;
                SelectedExcelBauteilnummern = _excelSelection.ToList();
                SelectedExcludedBauteilnummern = _excludeSelection.ToList();
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.SetColumnSpan(buttons, 2);
            layout.Controls.Add(buttons, 0, 5);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(layout);
        }

        private void OpenExcelPicker()
        {
            var data = ExcelParser.FamilyData;
            if (data == null || data.Count == 0)
            {
                TaskDialog.Show(
                    "Aus Excel hinzufügen",
                    "Es sind noch keine Excel-Daten geladen. Bitte verwenden Sie zuerst den " +
                    "Befehl \"Add Parameters\", um die Excel-Datei einzulesen.");
                return;
            }

            using (var picker = new BauteilnummerCheckListDialog(
                "Bauteilnummern aus Excel",
                "Wählen Sie die Bauteilnummern aus der Excel-Liste, " +
                "die der Tabelle zusätzlich hinzugefügt werden sollen:",
                data.Keys,
                _excelSelection))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;

                _excelSelection.Clear();
                _excelSelection.AddRange(picker.SelectedBauteilnummern);
                UpdateExcelCountLabel();
            }
        }

        private void ExcludeCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressExcludeEvent) return;

            // Unchecking clears any prior exclusion.
            if (!_excludeCheck.Checked)
            {
                _excludeSelection.Clear();
                UpdateExcludeCountLabel();
                return;
            }

            var template = (_templateCombo.SelectedItem as TemplateItem)?.Template;
            if (template == null)
            {
                ResetExcludeCheck();
                return;
            }

            IReadOnlyList<string> inView = _inViewProvider?.Invoke(template) ?? new List<string>();
            if (inView.Count == 0)
            {
                TaskDialog.Show(
                    "Bauteilnummern ausschließen",
                    "In der aktiven Ansicht wurden keine Bauteilnummern gefunden, " +
                    "die ausgeschlossen werden könnten.");
                ResetExcludeCheck();
                return;
            }

            using (var picker = new BauteilnummerCheckListDialog(
                "Bauteilnummern ausschließen",
                "Wählen Sie die Bauteilnummern aus der aktiven Ansicht, " +
                "die NICHT in der Tabelle erscheinen sollen:",
                inView,
                _excludeSelection))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    // Cancelled → revert to the off state.
                    ResetExcludeCheck();
                    return;
                }

                _excludeSelection.Clear();
                _excludeSelection.AddRange(picker.SelectedBauteilnummern);
            }

            // Nothing chosen is equivalent to "don't exclude".
            if (_excludeSelection.Count == 0)
            {
                ResetExcludeCheck();
                return;
            }

            UpdateExcludeCountLabel();
        }

        private void ResetExcludeCheck()
        {
            _suppressExcludeEvent = true;
            _excludeCheck.Checked = false;
            _suppressExcludeEvent = false;
            _excludeSelection.Clear();
            UpdateExcludeCountLabel();
        }

        private void UpdateExcelCountLabel()
        {
            _excelCountLabel.Text = _excelSelection.Count == 0
                ? "Keine zusätzlichen Bauteilnummern ausgewählt."
                : $"{_excelSelection.Count} Bauteilnummer(n) ausgewählt.";
        }

        private void UpdateExcludeCountLabel()
        {
            _excludeCountLabel.Text = _excludeSelection.Count == 0
                ? "Keine Bauteilnummern ausgeschlossen."
                : $"{_excludeSelection.Count} Bauteilnummer(n) ausgeschlossen.";
        }

        private static Label MakeLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(3, 8, 3, 3),
        };

        private sealed class TemplateItem
        {
            public ITableTemplate Template { get; }
            public TemplateItem(ITableTemplate t) { Template = t; }
            public override string ToString() => Template?.Title ?? "(unbenannt)";
        }

        private sealed class ExporterItem
        {
            public ITableExporter Exporter { get; }
            public ExporterItem(ITableExporter e) { Exporter = e; }
            public override string ToString() => Exporter?.FormatName ?? "(unbekannt)";
        }
    }
}
