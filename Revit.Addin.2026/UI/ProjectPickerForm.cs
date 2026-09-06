using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Revit.Addin._2026.UI
{
    public sealed class ProjectPickerForm : Form
    {
        private readonly ComboBox _combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button _ok = new Button { Dock = DockStyle.Bottom, Text = "OK" };
        private readonly Button _cancel = new Button { Dock = DockStyle.Bottom, Text = "Cancel" };

        private readonly List<(int Id, string Name)> _projects;

        public int? SelectedProjectId { get; private set; }

        public ProjectPickerForm(List<(int ProjectId, string Name)> projects)
        {
            Text = "Select Project";
            Width = 420;
            Height = 140;

            _projects = projects.Select(p => (p.ProjectId, p.Name)).ToList();

            _combo.DataSource = _projects;
            _combo.DisplayMember = "Name";
            _combo.ValueMember = "Id";

            _ok.Click += (_, __) =>
            {
                if (_combo.SelectedItem is ValueTuple<int, string> item)
                {
                    SelectedProjectId = item.Item1;
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                }
                Close();
            };

            _cancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(_combo);
            Controls.Add(_cancel);
            Controls.Add(_ok);
        }
    }
}
