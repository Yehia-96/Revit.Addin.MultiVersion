using System;
using System.Windows.Forms;

namespace Revit.Addin._2027.UI
{
    /// <summary>
    /// Wraps Revit's main window handle so WinForms dialogs are owned by Revit instead of
    /// floating free — an unowned modal can end up behind the Revit window, which looks like
    /// a freeze during a long batch run.
    /// </summary>
    public sealed class RevitWindowOwner : IWin32Window
    {
        public RevitWindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; private set; }
    }
}
