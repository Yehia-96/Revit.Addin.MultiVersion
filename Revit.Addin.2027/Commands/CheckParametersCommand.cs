using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Parameters;
using Revit.Addin._2027.DatabaseLink;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Interop;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Database → Check parameters. Lists the MH database's parameters in the open model as Ready,
    /// Missing, Other GUID or Can't add, and adds the missing ones as project parameters from Studio's
    /// master shared parameter file, which the connection file names. Replace swaps Other GUID ones for
    /// the database's after asking, and once more for those that schedules or view filters use. Families
    /// aren't reloaded. It only reads the database.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CheckParametersCommand : IExternalCommand
    {
        private const string Title = "Check parameters";

        // Revit reads the master file from a copy here, so Studio can always replace its own; one copy per
        // Revit, so two Revits never write over each other's.
        private static readonly string CopyFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH", "RevitTools");

        private static readonly TimeSpan OldCopy = TimeSpan.FromDays(1);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication application = commandData.Application;
                Document document = application.ActiveUIDocument?.Document;
                if (document == null || document.IsFamilyDocument)
                {
                    Show("Open a project first", "Only a project model has project parameters to check.", TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Cancelled;
                }

                ConnectionFileResult file = ConnectionFile.Read(ConnectionFile.DefaultPath);
                if (!file.IsLoaded)
                {
                    Show("Can't use the connection file", file.Problem, TaskDialogIcon.TaskDialogIconWarning);
                    return Result.Cancelled;
                }

                MasterFileCopies.RemoveOld(CopyFolder, DateTime.UtcNow, OldCopy);
                string localCopy = MasterFileCopies.PathFor(CopyFolder, Process.GetCurrentProcess().Id);

                var actions = new ParameterActions(
                    () => ModelParameterScan.Read(document),
                    toAdd => ParameterAdder.Add(application.Application, document, localCopy, toAdd),
                    toReplace => ParameterUseScan.Find(document, toReplace),
                    toReplace => DeletionPreview.Find(document, toReplace),
                    (toReplace, alsoDelete) => ParameterAdder.Replace(application.Application, document, localCopy, toReplace, alsoDelete),
                    AskToReplace,
                    AskAboutUses,
                    category => CategoryLabel(document, category));
                var window = new CheckParametersWindow(
                    new SqlLinkDatabase(file.Settings), file.MasterParameterFile, localCopy, ModelParameterScan.Read(document), actions);
                new WindowInteropHelper(window).Owner = application.MainWindowHandle;
                window.ShowDialog();

                // Succeeded keeps what Add bound; with nothing added there is nothing to keep either way.
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // Replace's question, with Keep as the safe default: replacing deletes the old parameters' values.
        private static bool AskToReplace(string title, string question)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = title,
                MainContent = question,
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Replace them",
                "Delete the model's parameters and their values, and add the database's.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Keep them",
                "Leave the model's parameters as they are; the link leaves them empty.");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        // The command links Revit's dialog offers, in order, and what each answers.
        private static readonly TaskDialogCommandLinkId[] Links =
        {
            TaskDialogCommandLinkId.CommandLink1, TaskDialogCommandLinkId.CommandLink2, TaskDialogCommandLinkId.CommandLink3,
        };

        private static readonly TaskDialogResult[] LinkResults =
        {
            TaskDialogResult.CommandLink1, TaskDialogResult.CommandLink2, TaskDialogResult.CommandLink3,
        };

        // Replace's second question, when schedules or view filters use some of the parameters (Yehia,
        // 2026-11-22): replace them anyway, or not. Cancel is the safe default.
        private static ReplaceChoice AskAboutUses(ReplaceUses uses)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = ParameterCheckTexts.ReplaceUsedTitle(uses),
                MainContent = ParameterCheckTexts.ReplaceUsedQuestion(uses),
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            IReadOnlyList<ReplaceUsedAnswer> answers = ParameterCheckTexts.ReplaceUsedAnswers(uses);
            int offered = Math.Min(answers.Count, Links.Length);
            for (int i = 0; i < offered; i++)
                dialog.AddCommandLink(Links[i], answers[i].Label, answers[i].Note);

            int chosen = Array.IndexOf(LinkResults, dialog.Show());
            return chosen >= 0 && chosen < offered ? answers[chosen].Choice : ReplaceChoice.Cancel;
        }

        // Revit's own name for the category, in Revit's language, e.g. "Generic Models".
        private static string CategoryLabel(Document document, string builtInCategoryName)
        {
            BuiltInCategory builtIn;
            if (string.IsNullOrEmpty(builtInCategoryName)
                || !Enum.TryParse(builtInCategoryName, out builtIn)
                || !Enum.IsDefined(typeof(BuiltInCategory), builtIn))
                return string.IsNullOrEmpty(builtInCategoryName) ? "—" : builtInCategoryName;
            Category category = Category.GetCategory(document, builtIn);
            return category == null ? builtInCategoryName : category.Name;
        }

        private static void Show(string headline, string content, TaskDialogIcon icon)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = headline,
                MainContent = content,
                MainIcon = icon,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
        }
    }
}
