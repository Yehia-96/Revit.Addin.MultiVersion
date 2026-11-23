using Autodesk.Revit.UI;
using MH.RevitTools.Database.SigningIn;
using System;
using System.Diagnostics;

namespace Revit.Addin._2027.DatabaseLink.SignIn
{
    /// <summary>Keeps the Database panel's third row showing who is signed in (<see cref="SignInFace"/>).</summary>
    internal sealed class SignInRow
    {
        private readonly PushButton _button;
        private readonly SignInSession _session;

        /// <param name="button">The row, as added for nobody.</param>
        public SignInRow(PushButton button, SignInSession session)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _session.Changed += OnChanged;
        }

        // Signing in and out happens in a command, so this runs on Revit's main thread, in API context.
        private void OnChanged(object sender, EventArgs e)
        {
            try
            {
                SignInFace face = SignInFace.For(_session.Person);
                _button.ItemText = face.Text;
                _button.ToolTip = face.ToolTip;
            }
            catch (Exception ex)
            {
                // The row is only a label; whoever signed in is signed in.
                Trace.WriteLine("[Parameter Tools] The sign-in row couldn't be changed: " + ex.Message);
            }
        }
    }
}
