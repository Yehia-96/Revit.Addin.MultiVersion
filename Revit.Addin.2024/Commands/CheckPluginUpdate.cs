using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2024.Services;
using System;
using System.Diagnostics;
using System.IO;
using Cursor = System.Windows.Forms.Cursor;
using Cursors = System.Windows.Forms.Cursors;

// Keep identical in 2024, 2026 and 2027 but for the year.
namespace Revit.Addin._2024.Commands
{
    /// <summary>
    /// Functionality → Check for Updates. Offers the version in the synchronized Teams folder when it's newer than the
    /// installed one. The files are copied and checked at once; they're installed when Revit closes, by a script that
    /// waits until no Revit uses them and says how it went. The work is in <see cref="PluginUpdate"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CheckPluginUpdate : IExternalCommand
    {
        private const string Title = "Check for Updates";

        private const string TeamsFolderUrl =
            "https://mhing.sharepoint.com/:f:/s/bim/EnndXLE_cLxKkeXBRY7emxwBcR5kh1hDzeC59c-hW66Q7g?e=GhLeaP";

        private const string SyncInstructions =
            "1. Microsoft Teams opens in your browser. If you're asked, sign in with your work account.\n"
            + "2. Click 'Open in SharePoint' at the top of the Files tab.\n"
            + "3. In SharePoint, click 'Synchronisieren' (Sync) in the toolbar, and allow OneDrive to open.\n\n"
            + "Once the folder is on this computer, click Check for Updates again.";

        // The update that installs when Revit closes; null for none.
        private static PluginUpdatePaths _scheduled;
        private static string _scheduledVersion;
        private static bool _mayBeRefused;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                PluginUpdatePaths paths = PluginUpdatePaths.For(commandData.Application.Application.VersionNumber);
                if (_scheduled != null)
                {
                    ShowScheduled();
                    return Result.Succeeded;
                }

                var update = new PluginUpdate(paths);
                PluginUpdateCheck check = update.Check();
                switch (check.State)
                {
                    case PluginUpdateState.Newer:
                        return Offer(update, check, paths);
                    case PluginUpdateState.UpToDate:
                        Show("You're already using version " + check.Installed + ".", "It's the one in the Teams folder on this computer.");
                        return Result.Succeeded;
                    case PluginUpdateState.InstalledIsNewer:
                        Show("There's no newer version", check.Text);
                        return Result.Succeeded;
                    case PluginUpdateState.Waiting:
                        Show("An update is already waiting", check.Text);
                        return Result.Succeeded;
                    case PluginUpdateState.NoTeamsFolder:
                        OfferTeams();
                        return Result.Cancelled;
                    default:
                        Show("Can't check for updates now", check.Text);
                        return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Starts the scheduled update's script, which waits until Revit has closed. Called when Revit closes.</summary>
        public static void StartScheduledUpdate()
        {
            if (_scheduled == null)
                return;
            string script = _scheduled.Script;
            _scheduled = null;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = script,
                    WorkingDirectory = Environment.SystemDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                });
            }
            catch (Exception ex)
            {
                // Revit is closing, so there's nowhere to say it. The version stays as it is, and Check for Updates offers
                // the new one again.
                Trace.WriteLine("[Parameter Tools] The update wasn't started: " + ex.Message);
            }
        }

        private static Result Offer(PluginUpdate update, PluginUpdateCheck check, PluginUpdatePaths paths)
        {
            var question = new TaskDialog(Title)
            {
                MainInstruction = "Update from " + check.Installed + " to " + check.Published + "?",
                MainContent = "The files are copied now and installed when you close Revit " + paths.RevitYear + ".",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            if (question.Show() != TaskDialogResult.Yes)
                return Result.Cancelled;

            string problem;
            bool mayBeRefused;
            Cursor before = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                problem = update.Prepare(check);
                mayBeRefused = problem == null && update.NeedsAdministrator();
            }
            finally
            {
                Cursor.Current = before;
            }
            if (problem != null)
            {
                Show("The update couldn't be copied", problem);
                return Result.Cancelled;
            }

            _scheduled = paths;
            _scheduledVersion = check.Published;
            _mayBeRefused = mayBeRefused;
            ShowScheduled();
            return Result.Succeeded;
        }

        private static void ShowScheduled() =>
            Show("Version " + _scheduledVersion + " is ready to install",
                "Close every Revit " + _scheduled.RevitYear + " window. A small window then installs it and says how it went. Wait for that "
                + "before you start Revit again."
                + (_mayBeRefused
                    ? "\n\nThis Windows account may not be allowed to replace the installed files, as when an administrator installed them. If "
                        + "the window says NOT UPDATED, an administrator has to install the update with Install.cmd from the ZIP."
                    : ""));

        private static void OfferTeams()
        {
            var question = new TaskDialog(Title)
            {
                MainInstruction = "The Teams folder isn't on this computer",
                MainContent = "Updates come from the folder 31 BIM in Microsoft Teams, synchronized to this computer with OneDrive. "
                    + "Open it now to synchronize it?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            if (question.Show() != TaskDialogResult.Yes)
                return;
            Process.Start(new ProcessStartInfo { FileName = TeamsFolderUrl, UseShellExecute = true });
            Show("Synchronize the folder 31 BIM", SyncInstructions);
        }

        private static void Show(string headline, string text) =>
            new TaskDialog(Title) { MainInstruction = headline, MainContent = text, CommonButtons = TaskDialogCommonButtons.Close }.Show();
    }
}
