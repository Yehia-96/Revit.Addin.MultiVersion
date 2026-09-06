using Autodesk.Revit.DB;
using RevitUI = Autodesk.Revit.UI.Selection.Selection;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using Revit.Addin._2024.Commands;

namespace Revit.Addin._2024.UI
{
    public class BauteilnummerSelection : Form
    {

        public List<string> SelectedValues { get; private set; } = new List<string>();
        protected readonly Dictionary<string, CheckBox> checkboxMap = new Dictionary<string, CheckBox>();
        Button hide = new Button
        {
            Text = "Hide",
            Width = 150,
            Left = 20,
            DialogResult = DialogResult.OK
        };
        public BauteilnummerSelection(IEnumerable<string> values)
        {
            Text = "Select Bauteilnummer(s)";
            Width = 500;
            Height = 450;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 4,
                ColumnCount = 1
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new System.Windows.Forms.Label
            {
                Text = "Select one or more Bauteilnummer values to hide:",
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 5)
            };
            mainLayout.Controls.Add(titleLabel);

            var checkboxPanel = new FlowLayoutPanel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(5)
            };

            foreach (string val in values.OrderBy(v => v))
            {
                var cb = new CheckBox
                {
                    Text = val,
                    Tag = val,
                    AutoSize = true,
                    Margin = new Padding(3)
                };
                checkboxMap[val] = cb;
                checkboxPanel.Controls.Add(cb);
            }

            mainLayout.Controls.Add(checkboxPanel);

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(0),
                AutoSize = true
            };

            var btnUnhideAll = new Button
            {
                Text = "Unhide All",
                Left = 400,
                Width = 100,
                DialogResult = DialogResult.OK
            };
            btnUnhideAll.Click += (s, e) =>
            {
                SelectedValues.Clear();
                Close();
            };

            var btnOk = new Button
            {
                Text = "Isolate",
                Left = 160,
                Width = 120,
                DialogResult = DialogResult.OK
            };
            btnOk.Click += (s, e) =>
            {
                SelectedValues = checkboxMap
                    .Where(pair => pair.Value.Checked)
                    .Select(pair => pair.Key)
                    .ToList();

                Close();
            };
            hide.Click += (s,e) =>
            {
                SelectedValues = checkboxMap
                    .Where(pair => pair.Value.Checked)
                    .Select(pair => pair.Key)
                    .ToList();
                ShowByUniqueID.PermanentHideElements();
                Close();
            };

            buttonPanel.Controls.Add(btnOk);
            buttonPanel.Controls.Add(hide);
            buttonPanel.Controls.Add(btnUnhideAll);
            mainLayout.Controls.Add(buttonPanel);

            Controls.Add(mainLayout);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // BauteilnummerSelection
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "BauteilnummerSelection";
            this.Load += new System.EventHandler(this.BauteilnummerSelection_Load);
            this.ResumeLayout(false);

        }

        private void BauteilnummerSelection_Load(object sender, EventArgs e)
        {

        }
    }
    public class DecidingParameter : Form
    {
      public enum ParametersForHidingDecider
        {
            Bauteilnummer,
            Ort
        }  
      public bool _isUnhideAll = false;
      public ParametersForHidingDecider SelectedParameter { get; private set; }

      Button hideElements = new Button
        {
            Text = "Hide Elements",
            Width = 120,
            Left = 200,
            DialogResult = DialogResult.OK
        };
        
    public ComboBox ComboBox { get; private set; }
        public DecidingParameter()
        {
            Text = "Select a parameter to hide elements based on";
            Width = 450;
            Height = 200;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            ComboBox.Items.AddRange(Enum.GetNames(typeof(ParametersForHidingDecider)));
            ComboBox.SelectedIndex = 0; // Default to Bauteilnummer
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(0),
                AutoSize = true
            };
            var btnOk = new Button
            {
                Text = "OK",
                Width = 100,
                DialogResult = DialogResult.OK
            };
            btnOk.Click += (s, e) =>
            {
                SelectedParameter = (ParametersForHidingDecider)Enum.Parse(typeof(ParametersForHidingDecider), ComboBox.SelectedItem.ToString());
                Close();
            };
            var btnUnhideAll = new Button
            {
                Text = "Unhide All",
                Width = 100,
                Dock = DockStyle.Left,
            };
            btnUnhideAll.Click += (s, e) =>
            {
                ShowByUniqueID.PickElementsToUnhide();
                _isUnhideAll = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            hideElements.Click += (s,e) =>
            {
                
                ShowByUniqueID.PickElementsToHide();
                Close();
            };
           
            buttonPanel.Controls.Add(btnUnhideAll);
            buttonPanel.Controls.Add(btnOk);
            Controls.Add(ComboBox);
            Controls.Add(buttonPanel);
            buttonPanel.Controls.Add(hideElements);
        }
    }
}
