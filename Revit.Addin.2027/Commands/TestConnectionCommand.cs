using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Diagnostics;
using Revit.Addin._2027.DatabaseLink;
using Revit.Addin._2027.DatabaseLink.SignIn;
using System;
using System.Globalization;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Database → Test connection. Reads the connection file, connects to the MH database,
    /// runs one read-only query and says what happened. It is the database link's first step: it
    /// proves the database driver loads and connects inside Revit, and its details show which
    /// driver files Revit actually used. Later this button opens the MH Database pane.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TestConnectionCommand : IExternalCommand
    {
        private const string Title = "Test connection";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                ConnectionFileResult file = ConnectionFiles.Read();
                if (!file.IsLoaded)
                {
                    Show("Can't use the connection file", file.Problem, null, TaskDialogIcon.TaskDialogIconWarning);
                    return Result.Succeeded;
                }

                ConnectionSettings settings = SignInGate.EnsurePassword(commandData.Application, file);
                if (settings == null)
                    return Result.Cancelled;

                ConnectionTestResult result = ConnectionTester.Run(settings, ConnectionTester.DefaultTimeoutSeconds);
                if (result.IsLoginRefused)
                    SignInGate.RefusePassword(settings);
                string background = "Connection file: " + file.Source + Environment.NewLine
                    + "Encryption: " + settings.DescribeEncryption() + Environment.NewLine
                    + SqlClientInfo.Describe();

                if (result.Succeeded)
                {
                    Show("Connected",
                        result.Database + " on " + result.Server + Environment.NewLine
                        + "Server login: " + result.SignedInAs + Environment.NewLine
                        + result.ServerVersion + " · " + Seconds(result.Elapsed),
                        background,
                        TaskDialogIcon.TaskDialogIconInformation);
                }
                else
                {
                    Show("Can't connect",
                        result.Problem + Environment.NewLine + Environment.NewLine
                        + "Tried " + result.Database + " on " + result.Server + " for " + Seconds(result.Elapsed) + ".",
                        result.Details + Environment.NewLine + Environment.NewLine + background,
                        TaskDialogIcon.TaskDialogIconWarning);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Seconds(TimeSpan elapsed) =>
            elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        private static void Show(string headline, string content, string details, TaskDialogIcon icon)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = headline,
                MainContent = content,
                MainIcon = icon,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            if (!string.IsNullOrEmpty(details))
                dialog.ExpandedContent = details;
            dialog.Show();
        }
    }
}
