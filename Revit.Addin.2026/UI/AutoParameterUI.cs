using Revit.Addin._2026.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Font = System.Drawing.Font;
using CheckBox = System.Windows.Forms.CheckBox;
namespace Revit.Addin._2026.UI
{
    public class AutoParameterUI : Form
    {
        private readonly Button btnRemove;
        private readonly Button btnAutoAdd;
        private readonly Button btnAddExcel;
        private readonly Button btnCleanUp;
        private readonly Button btnCancel;

        public SpecificSelection SpecificSelection { get; set; }
        public List<int> SelectedBauteilnummern { get; set; } = new List<int>();

        public Action OnRemoveClicked { get; set; }
        public Action OnAutoAddClicked { get; set; }
        public Action OnAddExcelClicked { get; set; }
        public Action OnCleanUpClicked { get; set; }

        public AutoParameterUI(List<int> selectedBauteilnummern)
        {
            SelectedBauteilnummern = selectedBauteilnummern ?? new List<int>();

            AutoScaleMode = AutoScaleMode.Dpi;
            this.Font = new System.Drawing.Font("Segoe UI", 9f);

            Text = "Parameter Tools";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Wider so controls never clip (and enforce it)
            ClientSize = new Size(520, 190);
            MinimumSize = new Size(520, 190);

            // Root layout
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                RowCount = 4,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // header
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // excel row
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));     // spacer
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // buttons

            var header = new Label
            {
                Text = "Choose an action:",
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };
            root.Controls.Add(header, 0, 0);

            // Excel row (fix clipping): label fills, button fixed width
            var excelRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 38,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            excelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            excelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // fixed button column

            var excelHint = new Label
            {
                Text = "Load data source before updating parameters.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 10, 0)
            };

            btnAddExcel = new Button
            {
                Text = "Add Excel…",
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                Margin = new Padding(0)
            };

            excelRow.Controls.Add(excelHint, 0, 0);
            excelRow.Controls.Add(btnAddExcel, 1, 0);
            root.Controls.Add(excelRow, 0, 1);

            // Bottom buttons: right-aligned via filler column
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 6,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // filler
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // Update
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // Add Parameters
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // Clean Up
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));  // Cancel
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));   // (keeps designer stable)

            const int H = 34;

            btnRemove = new Button
            {
                Text = "Remove",
                Height = H,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                Margin = new Padding(6, 0, 0, 0)
            };

            btnAutoAdd = new Button
            {
                Text = "Add Parameters…",
                Height = H,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                Margin = new Padding(6, 0, 0, 0)
            };

            btnCleanUp = new Button
            {
                Text = "Clean Up…",
                Height = H,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                Margin = new Padding(6, 0, 0, 0)
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                Height = H,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(6, 0, 0, 0)
            };

            // filler col = 0, buttons start at col 1
            buttons.Controls.Add(btnRemove, 1, 0);
            buttons.Controls.Add(btnAutoAdd, 2, 0);
            buttons.Controls.Add(btnCleanUp, 3, 0);
            buttons.Controls.Add(btnCancel, 4, 0);

            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            AcceptButton = btnRemove;
            CancelButton = btnCancel;

            // Handlers
            btnRemove.Click += (s, e) =>
            {
                OnRemoveClicked?.Invoke();
                DialogResult = DialogResult.OK;
                Close();
            };

            btnAddExcel.Click += (s, e) =>
            {
                OnAddExcelClicked?.Invoke();
                DialogResult = DialogResult.OK;
                Close();
            };

            btnAutoAdd.Click += (s, e) =>
            {
                var selectionDialog = new SpecificSelection(SelectedBauteilnummern);
                if (selectionDialog.ShowDialog(this) != DialogResult.OK)
                    return;

                SpecificSelection = selectionDialog;

                if (selectionDialog.AddAll || selectionDialog.SelectedBauteilnummern.Any())
                    OnAutoAddClicked?.Invoke();

                DialogResult = DialogResult.OK;
                Close();
            };

            btnCleanUp.Click += (s, e) =>
            {
               
                OnCleanUpClicked?.Invoke();
                DialogResult = DialogResult.OK;
                Close();
            };
        }
    }
}

public class SpecificSelection : Form
{
    private readonly Dictionary<int, CheckBox> checkboxMap = new Dictionary<int, CheckBox>();
    public List<int> SelectedBauteilnummern { get; private set; } = new List<int>();
    public bool AddAll { get; private set; } = false;

    public SpecificSelection(IEnumerable<int> bauteilnummern)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        this.Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "Bauteilnummer Selection";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        ClientSize = new Size(520, 640);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));     // list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // tools
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // separator spacing
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));         // buttons

        var header = new Label
        {
            Text = "Select one or more Bauteilnummer values:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(header, 0, 0);

        var checkboxPanel = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(6)
        };

        foreach (var num in (bauteilnummern ?? Enumerable.Empty<int>()).Distinct().OrderBy(n => n))
        {
            var cb = new CheckBox
            {
                Text = $"Bauteilnummer {num}",
                Tag = num,
                AutoSize = true
            };
            checkboxMap[num] = cb;
            checkboxPanel.Controls.Add(cb);
        }

        var listGroup = new GroupBox
        {
            Text = "Bauteilnummer List",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Padding = new Padding(10),
            Margin = new Padding(0)
        };
        listGroup.Controls.Add(checkboxPanel);
        root.Controls.Add(listGroup, 0, 1);

        // Tools row (left-aligned)
        var tools = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 8, 0, 0)
        };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var btnSelectAll = new Button { Text = "Select All", AutoSize = true, UseVisualStyleBackColor = true };
        btnSelectAll.Click += (s, e) => { foreach (var cb in checkboxMap.Values) cb.Checked = true; };

        var btnClearAll = new Button { Text = "Clear All", AutoSize = true, UseVisualStyleBackColor = true, Margin = new Padding(6, 0, 0, 0) };
        btnClearAll.Click += (s, e) => { foreach (var cb in checkboxMap.Values) cb.Checked = false; };

        tools.Controls.Add(btnSelectAll, 0, 0);
        tools.Controls.Add(btnClearAll, 1, 0);
        root.Controls.Add(tools, 0, 2);

        // Button bar (right-aligned, aligned via TableLayoutPanel)
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 10, 0, 0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // filler
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Add All
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Use Selection
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Cancel

        const int H = 34;

        var btnAddAll = new Button
        {
            Text = "Add All",
            Size = new Size(100, H),
            DialogResult = DialogResult.OK,
            UseVisualStyleBackColor = true,
            Margin = new Padding(6, 0, 0, 0)
        };
        btnAddAll.Click += (s, e) =>
        {
            AddAll = true;
            SelectedBauteilnummern = checkboxMap.Keys.ToList();
            Close();
        };

        var btnUseSelection = new Button
        {
            Text = "Use Selection",
            Size = new Size(140, H),
            DialogResult = DialogResult.OK,
            UseVisualStyleBackColor = true,
            Margin = new Padding(6, 0, 0, 0)
        };
        btnUseSelection.Click += (s, e) =>
        {
            AddAll = false;
            SelectedBauteilnummern = checkboxMap.Where(p => p.Value.Checked).Select(p => p.Key).ToList();
            Close();
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(90, H),
            DialogResult = DialogResult.Cancel,
            UseVisualStyleBackColor = true,
            Margin = new Padding(6, 0, 0, 0)
        };

        buttons.Controls.Add(btnAddAll, 1, 0);
        buttons.Controls.Add(btnUseSelection, 2, 0);
        buttons.Controls.Add(btnCancel, 3, 0);

        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);

        AcceptButton = btnUseSelection;
        CancelButton = btnCancel;
    }
}

public class ParameterRemoval : Form
{
    ListBox parameterListBox;
    Button btnRemove;
    Button btnAddName;
    public Action OnExecuteRemovalClicked { get; set; }

    public ParameterRemoval(HashSet<string> stagedParameters)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        this.Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "Remove Parameters";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(450, 500);

        // Main layout
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // list
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // add button
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // remove button

        // List box instead of checkboxes
        parameterListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiSimple,
            Margin = new Padding(0, 0, 0, 10)
        };
        PopulateParameterList(stagedParameters);

        // Add Name button
        btnAddName = new Button
        {
            Text = "Add Parameter Name",
            Height = 34,
            Dock = DockStyle.Fill,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        // Remove button
        btnRemove = new Button
        {
            Text = "Execute Removal",
            Height = 34,
            Dock = DockStyle.Fill,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0)
        };

        mainPanel.Controls.Add(parameterListBox, 0, 0);
        mainPanel.Controls.Add(btnAddName, 0, 1);
        mainPanel.Controls.Add(btnRemove, 0, 2);
        Controls.Add(mainPanel);

        btnAddName.Click += (s, e) =>
        {
            var inputDialog = new InputWindow(this);
            inputDialog.ShowDialog(this);
        };

        btnRemove.Click += (s, e) =>
        {
            OnExecuteRemovalClicked?.Invoke();
            Close();
        };
    }

    private void PopulateParameterList(HashSet<string> stagedParameters)
    {
        parameterListBox.Items.Clear();
        foreach (var name in (stagedParameters ?? Enumerable.Empty<string>()).Distinct().OrderBy(n => n))
        {
            parameterListBox.Items.Add(name);
        }
    }

    public void RefreshParameterList()
    {
        var currentParameters = ParameterCategoryUnassigner.GetParametersToRemoveFromFamily();
        PopulateParameterList(currentParameters);
    }

    public class InputWindow : Form
    {
        TextBox inputBox;
        Button btnOk;
        Button btnCancel;
        ParameterRemoval parentForm;

        public InputWindow(ParameterRemoval parent)
        {
            parentForm = parent;

            AutoScaleMode = AutoScaleMode.Dpi;
            this.Font = new System.Drawing.Font("Segoe UI", 9f);
            Text = "Input Parameter Name";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(400, 140);

            // Main layout
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var label = new Label
            {
                Text = "Enter parameter name:",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 8)
            };

            inputBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12)
            };

            // Button panel
            var buttonPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            btnOk = new Button
            {
                Text = "OK",
                Height = 34,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                DialogResult = DialogResult.OK,
                Margin = new Padding(0, 0, 6, 0)
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                Height = 34,
                Dock = DockStyle.Fill,
                UseVisualStyleBackColor = true,
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(6, 0, 0, 0)
            };

            buttonPanel.Controls.Add(btnOk, 0, 0);
            buttonPanel.Controls.Add(btnCancel, 1, 0);

            mainPanel.Controls.Add(label, 0, 0);
            mainPanel.Controls.Add(inputBox, 0, 1);
            mainPanel.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(mainPanel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            btnOk.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(inputBox.Text))
                {
                    ParameterCategoryUnassigner.PopulateRemovalHashSet(inputBox.Text);
                    parentForm.RefreshParameterList();
                    Close();
                }
            };
        }
    }
}

