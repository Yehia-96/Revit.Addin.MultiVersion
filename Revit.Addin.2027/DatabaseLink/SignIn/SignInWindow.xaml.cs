using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Revit.Addin._2027.DatabaseLink.SignIn
{
    /// <summary>
    /// Asks for the MH Database Studio account. Each click on Sign in is one try, made in the background so Revit never
    /// waits on the database. The password stays in the box and is gone with the window; nothing is saved.
    /// </summary>
    public partial class SignInWindow : Window
    {
        private static readonly Brush ProblemLine = DialogText.Frozen(0xE5, 0xA7, 0x9F);

        private readonly StudioSignIn _signIn;
        private readonly SignInWaits _waits = new SignInWaits();
        private readonly DispatcherTimer _clock;
        private bool _trying;
        private bool _saysWait;
        private bool _closed;

        /// <param name="signIn">Makes the tries; called on a background thread.</param>
        /// <param name="settings">The connection, to show which database it is.</param>
        public SignInWindow(StudioSignIn signIn, ConnectionSettings settings)
        {
            _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));

            InitializeComponent();
            Heading.Text = SignInTexts.Title;
            Subtitle.Text = SignInTexts.Subtitle;
            Connection.Text = SignInTexts.Connection(settings);
            ConnectionNote.Text = SignInTexts.ConnectionNote;
            FooterText.Text = SignInTexts.Footer;

            _clock = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            _clock.Tick += OnClock;
            ShowWait();
            Closed += OnClosed;
        }

        /// <summary>Who signed in; set only when the window closes with that.</summary>
        public SignedInPerson Person { get; private set; }

        private void OnSignIn(object sender, RoutedEventArgs e)
        {
            if (_trying || SecondsToWait() > 0)
                return;

            string userName = UserName.Text;
            string password = Password.Password;
            if (string.IsNullOrWhiteSpace(userName) || password.Length == 0)
            {
                ShowProblem(SignInTexts.Incomplete);
                (string.IsNullOrWhiteSpace(userName) ? (IInputElement)UserName : Password).Focus();
                return;
            }

            SetTrying(true);
            Task.Run(() => _signIn.Try(userName, password))
                .ContinueWith(tried => Dispatcher.BeginInvoke(new Action(() => OnTried(userName, tried))), TaskScheduler.Default);
        }

        private void OnTried(string userName, Task<SignInResult> tried)
        {
            // Look at the error even when the window is gone, so it never goes unobserved. Cancelled while it was
            // trying means nobody signed in, whatever the answer.
            Exception error = tried.IsFaulted ? tried.Exception.InnerException ?? tried.Exception : null;
            if (_closed)
                return;

            // This runs on Revit's thread outside the command's try/catch: a fault here must end up in the window.
            try
            {
                SetTrying(false);
                if (error == null)
                {
                    Take(userName, tried.Result);
                    return;
                }
                Trace.WriteLine("[Parameter Tools] The sign-in failed: " + error);
                ShowProblem(SignInTexts.Problem(error));
                Password.Focus();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The sign-in window failed: " + ex);
                ShowProblem(SignInTexts.Problem(ex));
            }
        }

        private void Take(string userName, SignInResult result)
        {
            if (result.Person != null)
            {
                Person = result.Person;
                DialogResult = true;
                return;
            }

            Password.Clear();
            Password.Focus();
            _waits.Remember(userName, result.WaitSeconds, DateTime.UtcNow);
            if (result.WaitSeconds == 0)
                ShowProblem(result.IsIncomplete ? SignInTexts.Incomplete : SignInTexts.Wrong);
            ShowWait();
        }

        // Another name doesn't have to wait for this one's tries.
        private void OnUserNameChanged(object sender, TextChangedEventArgs e) => ShowWait();

        private void OnClock(object sender, EventArgs e) => ShowWait();

        // While the database makes the name wait: how long, counted down, and no button.
        private void ShowWait()
        {
            // Called while the window is still being built, too.
            if (_clock == null)
                return;

            int seconds = SecondsToWait();
            SignInButton.Content = _trying ? "Signing in…" : SignInTexts.Button(seconds);
            SignInButton.IsEnabled = !_trying && seconds == 0;
            if (seconds > 0)
            {
                ShowProblem(SignInTexts.Wait(seconds));
                _saysWait = true;
                _clock.Start();
                return;
            }

            _clock.Stop();
            if (_saysWait)
                HideProblem();
        }

        private int SecondsToWait() => _waits.SecondsLeft(UserName.Text, DateTime.UtcNow);

        private void SetTrying(bool trying)
        {
            _trying = trying;
            UserName.IsEnabled = Password.IsEnabled = !trying;
            Cursor = trying ? Cursors.AppStarting : null;
            if (trying)
                HideProblem();
            ShowWait();
        }

        private void ShowProblem(string text)
        {
            _saysWait = false;
            ProblemText.Text = text;
            Problem.Visibility = Visibility.Visible;
            PasswordField.BorderBrush = ProblemLine;
        }

        private void HideProblem()
        {
            _saysWait = false;
            Problem.Visibility = Visibility.Collapsed;
            PasswordField.ClearValue(Border.BorderBrushProperty);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true;
            _clock.Stop();
            _clock.Tick -= OnClock;
            Password.Clear();
        }
    }
}
