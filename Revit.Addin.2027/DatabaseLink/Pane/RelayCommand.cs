using System;
using System.Windows.Input;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>A pane button's action. The pane runs on Revit's main thread, so the action does too.</summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _run;

        public RelayCommand(Action run)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        // Always enabled: the pane hides what can't be clicked instead.
        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _run();
    }
}
