using System;
using System.Drawing;
using System.Windows.Forms;

namespace Revit.Addin._2027.UI
{
    public class MaterialsUI : Form
    {
        private readonly Button populateBtn = CreatePrimaryButton("Populate Material Parameters", "Bind shared parameters and fill values from Excel.");
        private readonly Button createBtn = CreateSecondaryButton("Create Missing Materials", "Generate Revit materials with structural and visual defaults.");
        private readonly Button assignPatternBtn = CreateSecondaryButton("Assign Materials Pattern", "Assigns the patterns to the materials created from the excel sheet.");
        private readonly Label subtitleLabel = new Label();

        public Action PopulateMaterialsAction { get; set; }
        public Action CreateMaterialsAction { get; set; }

        public Action AssignPatternAction { get; set; }

        public MaterialsUI()
        {
            Text = "Material Automation";
            MinimumSize = new Size(620, 540);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            InitializeLayout();
            WireEvents();
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                AutoSize = true,
                Text = "Material Automation",
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(32, 43, 58),
                Margin = new Padding(0, 0, 0, 6)
            };

            subtitleLabel.AutoSize = true;
            subtitleLabel.Text = "Choose an action to create materials or populate material parameters.";
            subtitleLabel.ForeColor = Color.FromArgb(95, 108, 124);
            subtitleLabel.Margin = new Padding(0, 0, 0, 18);

            var cardPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16),
                Margin = new Padding(0),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };

            // IMPORTANT: fixed row heights
            cardPanel.RowStyles.Clear();
            cardPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            cardPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            cardPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

            populateBtn.Margin = new Padding(0, 0, 0, 10);
            createBtn.Margin = new Padding(0, 0, 0, 10);
            assignPatternBtn.Margin = new Padding(0);

            cardPanel.Controls.Add(populateBtn, 0, 0);
            cardPanel.Controls.Add(createBtn, 0, 1);
            cardPanel.Controls.Add(assignPatternBtn, 0, 2);

            var hintLabel = new Label
            {
                AutoSize = true,
                Text = "Tip: Run material creation first if the model does not contain all required materials.",
                ForeColor = Color.FromArgb(102, 116, 136),
                Margin = new Padding(0, 14, 0, 0)
            };

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(subtitleLabel, 0, 1);
            root.Controls.Add(cardPanel, 0, 2);
            root.Controls.Add(hintLabel, 0, 3);

            Controls.Add(root);
        }

        private void WireEvents()
        {
            populateBtn.Click += (sender, args) => ExecuteAction(PopulateMaterialsAction, "Populating material parameters...");
            createBtn.Click += (sender, args) => ExecuteAction(CreateMaterialsAction, "Creating materials...");
            assignPatternBtn.Click += (sender, args) => ExecuteAction(AssignPatternAction, "Assigning patterns to materials...");
        }

        private void ExecuteAction(Action action, string busyMessage)
        {
            if (action == null)
            {
                MessageBox.Show("This action is not configured.", "Material Automation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ToggleButtons(false);
            subtitleLabel.Text = busyMessage;

            try
            {
                action.Invoke();
                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                ToggleButtons(true);
            }
        }

        private void ToggleButtons(bool enabled)
        {
            populateBtn.Enabled = enabled;
            createBtn.Enabled = enabled;
            assignPatternBtn.Enabled = enabled;
            UseWaitCursor = !enabled;
        }

        private static Button CreatePrimaryButton(string title, string description)
        {
            return CreateActionButton(title, description, Color.FromArgb(41, 128, 185), Color.White);
        }

        private static Button CreateSecondaryButton(string title, string description)
        {
            return CreateActionButton(title, description, Color.FromArgb(236, 240, 245), Color.FromArgb(43, 53, 66));
        }

        private static Button CreateActionButton(string title, string description, Color backColor, Color foreColor)
        {
            var btn = new Button
            {
                AutoSize = false,
                Height = 92,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0)
            };

            btn.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 230);
            btn.FlatAppearance.BorderSize = 1;
            btn.Text = $"{title}{Environment.NewLine}{description}";

            return btn;
        }
    }
}
