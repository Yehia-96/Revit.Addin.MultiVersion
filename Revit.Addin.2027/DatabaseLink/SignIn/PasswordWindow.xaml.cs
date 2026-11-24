using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Revit.Addin._2027.DatabaseLink.SignIn
{
    /// <summary>
    /// Asks for the database password, which the connection file doesn't hold. Each click on Continue is one try, made in
    /// the background so Revit never waits on the database; only a password that works closes the window. The window
    /// keeps nothing: what happens to the password is up to its caller.
    /// </summary>
    public partial class PasswordWindow : Window
    {
        private static readonly Brush ProblemLine = DialogText.Frozen(0xE5, 0xA7, 0x9F);

        private readonly ConnectionSettings _settings;
        private readonly Func<ConnectionSettings, ConnectionTestResult> _test;
        private bool _trying;
        private bool _closed;

        /// <param name="settings">The connection the password is for.</param>
        /// <param name="wasRefused">The server refused the one that was kept.</param>
        /// <param name="test">Makes the tries; called on a background thread.</param>
        public PasswordWindow(ConnectionSettings settings, bool wasRefused, Func<ConnectionSettings, ConnectionTestResult> test)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _test = test ?? throw new ArgumentNullException(nameof(test));

            InitializeComponent();
            Heading.Text = PasswordTexts.Title;
            Subtitle.Text = PasswordTexts.Subtitle;
            Connection.Text = SignInTexts.Connection(settings);
            Note.Text = PasswordTexts.Note;
            FooterText.Text = PasswordTexts.Footer;
            ContinueButton.Content = PasswordTexts.Button;
            if (wasRefused)
                ShowProblem(PasswordTexts.RefusedBefore);
            Closed += OnClosed;
        }

        /// <summary>The connection with the password in it; set only when the window closes with one that works.</summary>
        public ConnectionSettings Settings { get; private set; }

        private void OnContinue(object sender, RoutedEventArgs e)
        {
            if (_trying)
                return;

            // Copied from a message, where a space or a line break easily comes along.
            string password = Password.Password.Trim();
            if (password.Length == 0)
            {
                ShowProblem(PasswordTexts.Incomplete);
                Password.Focus();
                return;
            }

            ConnectionSettings entered = _settings.WithPassword(password);
            SetTrying(true);
            Task.Run(() => _test(entered))
                .ContinueWith(tested => Dispatcher.BeginInvoke(new Action(() => OnTested(entered, tested))), TaskScheduler.Default);
        }

        private void OnTested(ConnectionSettings entered, Task<ConnectionTestResult> tested)
        {
            // Look at the error even when the window is gone, so it never goes unobserved. Cancelled while it was trying
            // means no password, whatever the answer.
            Exception error = tested.IsFaulted ? tested.Exception.InnerException ?? tested.Exception : null;
            if (_closed)
                return;

            // This runs on Revit's thread outside the command's try/catch: a fault here must end up in the window.
            try
            {
                SetTrying(false);
                if (error != null)
                {
                    Trace.WriteLine("[Parameter Tools] The database password couldn't be checked: " + error);
                    ShowProblem("Something went wrong in the add-in: " + error.Message);
                    return;
                }

                if (tested.Result.Succeeded)
                {
                    Settings = entered;
                    DialogResult = true;
                    return;
                }

                ShowProblem(PasswordTexts.Problem(tested.Result));
                Password.Focus();
                Password.SelectAll();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The password window failed: " + ex);
                ShowProblem("Something went wrong in the add-in: " + ex.Message);
            }
        }

        private void SetTrying(bool trying)
        {
            _trying = trying;
            Password.IsEnabled = !trying;
            ContinueButton.IsEnabled = !trying;
            ContinueButton.Content = trying ? PasswordTexts.Trying : PasswordTexts.Button;
            Cursor = trying ? Cursors.AppStarting : null;
            if (trying)
                HideProblem();
        }

        private void ShowProblem(string text)
        {
            ProblemText.Text = text;
            Problem.Visibility = Visibility.Visible;
            PasswordField.BorderBrush = ProblemLine;
        }

        private void HideProblem()
        {
            Problem.Visibility = Visibility.Collapsed;
            PasswordField.ClearValue(Border.BorderBrushProperty);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true;
            Password.Clear();
        }
    }
}
