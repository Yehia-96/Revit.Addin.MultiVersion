using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Comparing;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using Microsoft.Win32;
using Revit.Addin._2027.DatabaseLink;
using Revit.Addin._2027.DatabaseLink.Compare;
using Revit.Addin._2027.DatabaseLink.SignIn;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Interop;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Database → Compare with Excel (step 7, the acceptance). Loads a Planungsdatenbank workbook the way the Excel
    /// import does, and the open model's project from the MH database, and lists every difference: objects,
    /// parameters, values after unit conversion, and materials. It changes nothing. Temporary: it goes once both
    /// give the same.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CompareWithExcelCommand : IExternalCommand
    {
        private const string Title = "Compare with Excel";

        // Where the last workbook was, for the next time.
        private static string _folder;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication application = commandData.Application;
                Document document = application.ActiveUIDocument?.Document;
                if (document == null || document.IsFamilyDocument)
                    return Tell("Open a project first", "The comparison uses the project the model is linked to.");
                ModelLink link = ProjectLinkStorage.Read(document);
                if (link == null)
                    return Tell("Link the model first", "The comparison uses the project the model is linked to. Link it in the MH Database pane.");
                ConnectionFileResult file = ConnectionFiles.Read();
                if (!file.IsLoaded)
                    return Tell("Can't use the connection file", file.Problem);

                ConnectionSettings settings = SignInGate.Ensure(application, file);
                if (settings == null)
                    return Result.Cancelled;

                string path = AskForWorkbook();
                if (path == null)
                    return Result.Cancelled;

                // What's typed into an open workbook and not saved yet isn't compared, so the window says when it was saved.
                string saved = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                LoadComparison comparison;
                try
                {
                    comparison = Compare(path, new LoadedValues(document), settings, link);
                }
                catch (LinkDatabaseException ex)
                {
                    return Tell("Can't read the project from the database", ex.Message);
                }
                catch (WorkbookException ex)
                {
                    return Tell("Can't read the workbook", ex.Message);
                }

                var window = new CompareWindow(comparison,
                    Path.GetFileName(path) + " as saved " + saved + ", and " + link.Describe() + " in the MH database");
                new WindowInteropHelper(window).Owner = application.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static LoadComparison Compare(string path, LoadedValues values, ConnectionSettings settings, ModelLink link)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                LoadedProject database = DatabaseLoad.From(ReadProject(settings, link), values.Convert, LoadedValues.KindOf);
                return LoadComparison.Of(ReadWorkbook(path, values), database);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // The project the model is linked to, if the saved id still means it.
        private static LinkProjectData ReadProject(ConnectionSettings settings, ModelLink link)
        {
            ILinkDatabase database = ConnectionFiles.Open(settings);
            if (link.Check(database.GetProjects()).Status != ModelLinkStatus.Matches)
                throw new LinkDatabaseException("This database doesn't have the project " + link.Describe()
                    + " under the model's id any more. The MH Database pane says more.");
            LinkProjectData data = database.GetProjectData(link.ProjectId);
            if (data == null)
                throw new LinkDatabaseException("This database doesn't have the project " + link.Describe() + " any more.");
            return data;
        }

        private static LoadedProject ReadWorkbook(string path, LoadedValues values)
        {
            try
            {
                return ExcelLoad.Read(path, values.Convert, LoadedValues.KindOf);
            }
            catch (Exception ex)
            {
                // Anything a workbook of another shape can cause; the message is all there is to say.
                throw new WorkbookException(ex.Message, ex);
            }
        }

        private static string AskForWorkbook()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Planungsdatenbank to compare with the MH database",
                Filter = "Excel workbooks|*.xlsx;*.xlsm",
                InitialDirectory = _folder ?? "",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true)
                return null;
            _folder = Path.GetDirectoryName(dialog.FileName);
            return dialog.FileName;
        }

        private static Result Tell(string headline, string content)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = headline,
                MainContent = content,
                MainIcon = TaskDialogIcon.TaskDialogIconInformation,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
            return Result.Cancelled;
        }

        private sealed class WorkbookException : Exception
        {
            public WorkbookException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
    }
}
