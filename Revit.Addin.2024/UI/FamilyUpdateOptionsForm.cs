using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Revit.Addin._2024.Services.FamilyUpdate;

namespace Revit.Addin._2024.UI
{
    /// <summary>
    /// Collects the settings for a batch family update: which folder, which steps, and where the
    /// results go.
    /// </summary>
    public sealed class FamilyUpdateOptionsForm : Form
    {
        private readonly IList<FamilyUpdateStepDescriptor> _descriptors;

        private readonly TextBox _sourceBox = new TextBox();
        private readonly CheckBox _includeSubfolders = new CheckBox();
        private readonly CheckedListBox _stepList = new CheckedListBox();
        private readonly Label _stepDescription = new Label();
        private readonly CheckBox _keepOriginals = new CheckBox();
        private readonly Label _outputLabel = new Label();
        private readonly TextBox _outputBox = new TextBox();
        private readonly Button _outputBrowse = new Button();
        private readonly Label _outputHint = new Label();
        private readonly CheckBox _skipCurrent = new CheckBox();
        private readonly CheckBox _compact = new CheckBox();
        private readonly CheckBox _copyCatalogs = new CheckBox();
        private readonly CheckBox _audit = new CheckBox();

        public FamilyUpdateOptionsForm(IList<FamilyUpdateStepDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException("descriptors");
            }

            _descriptors = descriptors;

            Text = "Update Family Files";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(564, 540);

            BuildSourceGroup();
            BuildStepsGroup();
            BuildOutputGroup();
            BuildAdvancedGroup();
            BuildButtons();

            ApplyDefaults();
            UpdateOutputEnabledState();
        }

        /// <summary>Valid only when the dialog closed with <see cref="DialogResult.OK"/>.</summary>
        public FamilyUpdateOptions Options { get; private set; }

        /// <summary>The steps the user ticked, already built against <see cref="Options"/>.</summary>
        public IList<IFamilyFileStep> SelectedSteps { get; private set; }

        private void BuildSourceGroup()
        {
            var group = new GroupBox
            {
                Text = "1. Family library",
                Location = new Point(12, 10),
                Size = new Size(540, 92)
            };

            group.Controls.Add(new Label
            {
                Text = "Folder:",
                Location = new Point(14, 29),
                Size = new Size(50, 20)
            });

            _sourceBox.Location = new Point(66, 26);
            _sourceBox.Size = new Size(366, 22);
            group.Controls.Add(_sourceBox);

            var browse = new Button
            {
                Text = "Browse...",
                Location = new Point(440, 25),
                Size = new Size(86, 24)
            };
            browse.Click += (sender, args) => BrowseInto(_sourceBox, "Select the folder that holds the family files");
            group.Controls.Add(browse);

            _includeSubfolders.Text = "Include subfolders";
            _includeSubfolders.Location = new Point(66, 58);
            _includeSubfolders.Size = new Size(200, 22);
            group.Controls.Add(_includeSubfolders);

            Controls.Add(group);
        }

        private void BuildStepsGroup()
        {
            var group = new GroupBox
            {
                Text = "2. Steps to apply",
                Location = new Point(12, 110),
                Size = new Size(540, 122)
            };

            _stepList.Location = new Point(14, 24);
            _stepList.Size = new Size(512, 56);
            _stepList.CheckOnClick = true;
            _stepList.IntegralHeight = false;
            _stepList.SelectedIndexChanged += (sender, args) => ShowSelectedStepDescription();
            group.Controls.Add(_stepList);

            _stepDescription.Location = new Point(14, 86);
            _stepDescription.Size = new Size(512, 30);
            _stepDescription.ForeColor = SystemColors.GrayText;
            group.Controls.Add(_stepDescription);

            Controls.Add(group);
        }

        private void BuildOutputGroup()
        {
            var group = new GroupBox
            {
                Text = "3. Where the updated files go",
                Location = new Point(12, 240),
                Size = new Size(540, 128)
            };

            _keepOriginals.Text = "Keep the originals - write upgraded copies to another folder";
            _keepOriginals.Location = new Point(14, 24);
            _keepOriginals.Size = new Size(512, 22);
            _keepOriginals.CheckedChanged += (sender, args) => UpdateOutputEnabledState();
            group.Controls.Add(_keepOriginals);

            _outputLabel.Text = "Output folder:";
            _outputLabel.Location = new Point(34, 56);
            _outputLabel.Size = new Size(90, 20);
            group.Controls.Add(_outputLabel);

            _outputBox.Location = new Point(126, 53);
            _outputBox.Size = new Size(306, 22);
            group.Controls.Add(_outputBox);

            _outputBrowse.Text = "Browse...";
            _outputBrowse.Location = new Point(440, 52);
            _outputBrowse.Size = new Size(86, 24);
            _outputBrowse.Click += (sender, args) => BrowseInto(_outputBox, "Select the folder for the upgraded copies");
            group.Controls.Add(_outputBrowse);

            _outputHint.Location = new Point(14, 84);
            _outputHint.Size = new Size(512, 36);
            _outputHint.ForeColor = SystemColors.GrayText;
            group.Controls.Add(_outputHint);

            Controls.Add(group);
        }

        private void BuildAdvancedGroup()
        {
            var group = new GroupBox
            {
                Text = "4. Advanced",
                Location = new Point(12, 376),
                Size = new Size(540, 116)
            };

            _skipCurrent.Text = "Skip files already saved in this Revit version (much faster)";
            _skipCurrent.Location = new Point(14, 22);
            _skipCurrent.Size = new Size(512, 22);
            group.Controls.Add(_skipCurrent);

            _compact.Text = "Compact files on save";
            _compact.Location = new Point(14, 44);
            _compact.Size = new Size(512, 22);
            group.Controls.Add(_compact);

            _copyCatalogs.Text = "Copy type catalogs (.txt) alongside the families";
            _copyCatalogs.Location = new Point(14, 66);
            _copyCatalogs.Size = new Size(512, 22);
            group.Controls.Add(_copyCatalogs);

            _audit.Text = "Audit each file while opening (repairs corruption, much slower)";
            _audit.Location = new Point(14, 88);
            _audit.Size = new Size(512, 22);
            group.Controls.Add(_audit);

            Controls.Add(group);
        }

        private void BuildButtons()
        {
            var ok = new Button
            {
                Text = "Start",
                Location = new Point(376, 502),
                Size = new Size(86, 26),
                DialogResult = DialogResult.None
            };
            ok.Click += (sender, args) => OnStartClicked();

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(466, 502),
                Size = new Size(86, 26),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void ApplyDefaults()
        {
            FamilyUpdateOptions defaults = FamilyUpdateOptions.CreateDefault();

            _includeSubfolders.Checked = defaults.IncludeSubfolders;
            _skipCurrent.Checked = defaults.SkipFilesAlreadyCurrent;
            _compact.Checked = defaults.CompactOnSave;
            _copyCatalogs.Checked = defaults.CopyTypeCatalogs;
            _audit.Checked = defaults.AuditOnOpen;
            _keepOriginals.Checked = defaults.OutputMode == FamilyOutputMode.CopyToSeparateFolder;

            foreach (FamilyUpdateStepDescriptor descriptor in _descriptors)
            {
                _stepList.Items.Add(descriptor, descriptor.EnabledByDefault);
            }

            if (_stepList.Items.Count > 0)
            {
                _stepList.SelectedIndex = 0;
            }
        }

        private void ShowSelectedStepDescription()
        {
            var descriptor = _stepList.SelectedItem as FamilyUpdateStepDescriptor;
            _stepDescription.Text = descriptor == null ? string.Empty : descriptor.Description;
        }

        private void UpdateOutputEnabledState()
        {
            bool keepOriginals = _keepOriginals.Checked;

            _outputLabel.Enabled = keepOriginals;
            _outputBox.Enabled = keepOriginals;
            _outputBrowse.Enabled = keepOriginals;

            _outputHint.Text = keepOriginals
                ? "The source folder is not modified. Upgraded copies are written to the output "
                  + "folder, mirroring the source folder structure and keeping the original file names."
                : "The original .rfa files are overwritten. Each one is copied into a timestamped "
                  + "backup folder inside the source folder first.";
        }

        private void BrowseInto(TextBox target, string description)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = description;
                dialog.ShowNewFolderButton = true;

                if (!string.IsNullOrWhiteSpace(target.Text))
                {
                    dialog.SelectedPath = target.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnStartClicked()
        {
            var options = new FamilyUpdateOptions
            {
                SourceFolder = _sourceBox.Text.Trim(),
                IncludeSubfolders = _includeSubfolders.Checked,
                SkipFilesAlreadyCurrent = _skipCurrent.Checked,
                AuditOnOpen = _audit.Checked,
                CompactOnSave = _compact.Checked,
                CopyTypeCatalogs = _copyCatalogs.Checked,
                OutputMode = _keepOriginals.Checked
                    ? FamilyOutputMode.CopyToSeparateFolder
                    : FamilyOutputMode.InPlaceWithBackup,
                OutputFolder = _outputBox.Text.Trim()
            };

            string error = options.Validate();
            if (error != null)
            {
                ShowValidationError(error);
                return;
            }

            var steps = new List<IFamilyFileStep>();
            foreach (object item in _stepList.CheckedItems)
            {
                var descriptor = item as FamilyUpdateStepDescriptor;
                if (descriptor != null)
                {
                    steps.Add(descriptor.Create(options));
                }
            }

            if (steps.Count == 0)
            {
                ShowValidationError("Tick at least one step to apply.");
                return;
            }

            if (options.OutputMode == FamilyOutputMode.InPlaceWithBackup && !ConfirmOverwrite(options))
            {
                return;
            }

            Options = options;
            SelectedSteps = steps;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ConfirmOverwrite(FamilyUpdateOptions options)
        {
            string message = string.Format(
                "The original .rfa files in{0}{0}{1}{0}{0}will be overwritten.{0}{0}"
                + "A copy of every file is placed in a timestamped backup folder inside that "
                + "same folder before it is changed.{0}{0}Continue?",
                Environment.NewLine,
                options.SourceFolder);

            return MessageBox.Show(
                this,
                message,
                "Overwrite the originals?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void ShowValidationError(string error)
        {
            MessageBox.Show(this, error, "Update Family Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
