using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// The docked "MH Database" pane. Revit takes panes only while it starts, so it's registered then; the
    /// big Database button shows and hides it. The first time it docks on the right; after that Revit
    /// remembers where each person put it.
    /// </summary>
    internal sealed class DatabasePane : IDockablePaneProvider
    {
        public const string Title = "MH Database";

        // Never change it: Revit remembers each person's place for the pane by this id.
        public static readonly DockablePaneId Id = new DockablePaneId(new Guid("5d0b7c55-2f4e-4a8a-9d43-8c1f0e6b2a71"));

        private readonly FrameworkElement _view;

        private DatabasePane(FrameworkElement view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        /// <summary>Why the pane couldn't start with Revit; null when it did.</summary>
        public static string StartProblem { get; set; }

        public static void Register(UIControlledApplication application, FrameworkElement view) =>
            application.RegisterDockablePane(Id, Title, new DatabasePane(view));

        public static bool IsRegistered => DockablePane.PaneIsRegistered(Id);

        /// <summary>Shows the pane, or hides it when it's showing.</summary>
        public static void Toggle(UIApplication application)
        {
            DockablePane pane = application.GetDockablePane(Id);
            if (pane.IsShown())
                pane.Hide();
            else
                pane.Show();
        }

        public static void Show(UIApplication application) => application.GetDockablePane(Id).Show();

        public static bool IsShown(UIApplication application) => IsRegistered && application.GetDockablePane(Id).IsShown();

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.VisibleByDefault = false;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                MinimumWidth = 300,
            };
        }
    }
}
