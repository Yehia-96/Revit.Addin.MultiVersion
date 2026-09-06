using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Revit.Addin._2026.Services.FamilyUpdate;

namespace Revit.Addin._2026.UI
{
    /// <summary>
    /// Drives a batch run and shows its progress, with a Cancel button that actually stops the
    /// run between files.
    /// </summary>
    /// <remarks>
    /// The work runs on the UI thread on purpose: the Revit API may only be called from the
    /// thread that raised the external command, so a background worker is not an option here.
    /// Pumping the message queue between files is what keeps Cancel clickable.
    /// </remarks>
    public sealed class FamilyUpdateProgressForm : Form
    {
        private readonly FamilyUpdateRunner _runner;

        private readonly Label _headline = new Label();
        private readonly Label _currentFile = new Label();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Label _counts = new Label();
        private readonly Button _cancelButton = new Button();

        private bool _cancelRequested;
        private bool _isRunning;

        public FamilyUpdateProgressForm(FamilyUpdateRunner runner)
        {
            if (runner == null)
            {
                throw new ArgumentNullException("runner");
            }

            _runner = runner;

            Text = "Updating family files";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 168);

            BuildControls();

            Shown += OnShown;
            FormClosing += OnFormClosing;
        }

        /// <summary>Null when the run never started because no family files were found.</summary>
        public FamilyUpdateReport Report { get; private set; }

        /// <summary>True when discovery turned up no .rfa files at all.</summary>
        public bool FoundNoFiles { get; private set; }

        private void BuildControls()
        {
            _headline.Location = new Point(14, 14);
            _headline.Size = new Size(492, 20);
            _headline.Text = "Scanning folder...";
            Controls.Add(_headline);

            _currentFile.Location = new Point(14, 38);
            _currentFile.Size = new Size(492, 20);
            _currentFile.AutoEllipsis = true;
            _currentFile.ForeColor = SystemColors.GrayText;
            Controls.Add(_currentFile);

            _progressBar.Location = new Point(14, 66);
            _progressBar.Size = new Size(492, 22);
            _progressBar.Style = ProgressBarStyle.Marquee;
            Controls.Add(_progressBar);

            _counts.Location = new Point(14, 96);
            _counts.Size = new Size(492, 20);
            _counts.ForeColor = SystemColors.GrayText;
            Controls.Add(_counts);

            _cancelButton.Text = "Cancel";
            _cancelButton.Location = new Point(414, 126);
            _cancelButton.Size = new Size(92, 26);
            _cancelButton.Click += (sender, args) => RequestCancel();
            Controls.Add(_cancelButton);
        }

        private void OnShown(object sender, EventArgs args)
        {
            // Hand control back to the message loop first so the form paints before the run
            // begins; otherwise the user stares at an empty window during discovery.
            BeginInvoke(new Action(RunBatch));
        }

        private void RunBatch()
        {
            _isRunning = true;

            try
            {
                IList<FamilyFileCandidate> candidates = _runner.Discover();

                if (candidates.Count == 0)
                {
                    FoundNoFiles = true;
                    DialogResult = DialogResult.OK;
                    return;
                }

                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Minimum = 0;
                _progressBar.Maximum = candidates.Count;
                _headline.Text = string.Format("Processing {0} family files...", candidates.Count);

                Report = _runner.Run(candidates, OnProgress, () => _cancelRequested);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "The batch stopped unexpectedly:" + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Update Family Files",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                DialogResult = DialogResult.Abort;
            }
            finally
            {
                _isRunning = false;
                Close();
            }
        }

        private void OnProgress(int completed, int total, string currentFileName)
        {
            _progressBar.Value = Math.Min(completed, _progressBar.Maximum);
            _counts.Text = string.Format("{0} of {1} processed", completed, total);
            _currentFile.Text = string.IsNullOrEmpty(currentFileName) ? string.Empty : currentFileName;

            // Lets the queued paint and click messages run, which is what makes Cancel work.
            Application.DoEvents();
        }

        private void RequestCancel()
        {
            _cancelRequested = true;
            _cancelButton.Enabled = false;
            _headline.Text = "Cancelling after the current file...";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs args)
        {
            // The run owns an open Revit document; letting the window close underneath it would
            // leak that handle. Treat any close attempt during the run as a cancel request.
            if (_isRunning && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                RequestCancel();
            }
        }
    }
}
