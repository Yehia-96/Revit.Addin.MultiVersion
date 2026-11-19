using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using Revit.Addin._2027.DatabaseLink;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Interop;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Database → Link project. Asks which MH database project the open model is, suggesting one from
    /// its Project Number, and saves the answer in the model (<see cref="ProjectLinkStorage"/>), so
    /// everyone who opens it works with the same project. It only reads the database.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LinkProjectCommand : IExternalCommand
    {
        private const string Title = "Link project";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication application = commandData.Application;
                Document document = application.ActiveUIDocument?.Document;
                if (document == null || document.IsFamilyDocument)
                {
                    Show("Open a project first", "Only a project model can be linked to an MH database project.",
                        TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Cancelled;
                }

                ConnectionFileResult file = ConnectionFile.Read(ConnectionFile.DefaultPath);
                if (!file.IsLoaded)
                {
                    Show("Can't use the connection file", file.Problem, TaskDialogIcon.TaskDialogIconWarning);
                    return Result.Cancelled;
                }

                LinkProject chosen = AskForProject(application, document, new SqlLinkDatabase(file.Settings));
                if (chosen == null)
                    return Result.Cancelled;

                ModelLink link = ModelLink.To(chosen, DateTime.UtcNow, application.Application.Username);
                return SaveLink(document, link) ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static LinkProject AskForProject(UIApplication application, Document document, ILinkDatabase database)
        {
            var window = new LinkProjectWindow(
                ModelName(document), document.ProjectInformation.Number, ProjectLinkStorage.Read(document), database);
            new WindowInteropHelper(window).Owner = application.MainWindowHandle;
            return window.ShowDialog() == true ? window.ChosenProject : null;
        }

        private static bool SaveLink(Document document, ModelLink link)
        {
            string blocked = WhyTheLinkCantBeSaved(document);
            if (blocked != null)
            {
                Show("Can't save the link now", blocked, TaskDialogIcon.TaskDialogIconWarning);
                return false;
            }

            using (var transaction = new Transaction(document, "Link to MH project"))
            {
                transaction.Start();
                try
                {
                    ProjectLinkStorage.Write(document, link);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    transaction.RollBack();
                    Show("The link wasn't saved", "Revit refused the change: " + ex.Message, TaskDialogIcon.TaskDialogIconWarning);
                    return false;
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    Show("The link wasn't saved", "Revit undid the change; its own message said why. The model isn't linked.",
                        TaskDialogIcon.TaskDialogIconWarning);
                    return false;
                }
            }

            Show("Linked to " + link.Describe(),
                document.IsWorkshared
                    ? "Synchronize with central to share the link with everyone who opens the model."
                    : "Save the model to keep the link.",
                TaskDialogIcon.TaskDialogIconInformation);
            return true;
        }

        private static string WhyTheLinkCantBeSaved(Document document)
        {
            if (document.IsReadOnly)
                return "The model is open read-only.";
            if (!document.IsWorkshared)
                return null;

            // Borrow Project Information first, so a model that someone else is editing, or one that is
            // behind the central model, gets a plain answer instead of a failed change.
            ElementId projectInformation = document.ProjectInformation.Id;
            ICollection<ElementId> borrowed;
            try
            {
                borrowed = WorksharingUtils.CheckoutElements(document, new List<ElementId> { projectInformation });
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return "Revit couldn't borrow Project Information from the central model: " + ex.Message;
            }
            if (borrowed.Contains(projectInformation))
                return null;

            string owner;
            if (WorksharingUtils.GetCheckoutStatus(document, projectInformation, out owner) == CheckoutStatus.OwnedByOtherUser)
                return owner + " is editing Project Information. Ask them to synchronize with central, then link again.";
            return "Project Information has changed in the central model. Reload Latest (Collaborate tab), then link again.";
        }

        // The file name, as the dialog shows it; an unsaved model has only its title.
        private static string ModelName(Document document) =>
            string.IsNullOrEmpty(document.PathName) ? document.Title : Path.GetFileName(document.PathName);

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
