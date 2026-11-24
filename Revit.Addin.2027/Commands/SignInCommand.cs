using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.SigningIn;
using Revit.Addin._2027.DatabaseLink;
using Revit.Addin._2027.DatabaseLink.SignIn;
using System;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// The Database panel's third row and the pane's "Sign in…": signs in to the MH database with a Studio account. With
    /// someone signed in, it says who, and offers to sign out or to sign in as someone else.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SignInCommand : IExternalCommand
    {
        private const string Title = "Sign in";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Run(commandData.Application, ref message);

        /// <summary>The whole command, for the pane's button too. Call in API context.</summary>
        public static Result Run(UIApplication application, ref string message)
        {
            try
            {
                ConnectionFileResult file = ConnectionFiles.Read();
                if (!file.IsLoaded)
                {
                    var dialog = new TaskDialog(Title)
                    {
                        MainInstruction = "Can't use the connection file",
                        MainContent = file.Problem,
                        MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                        CommonButtons = TaskDialogCommonButtons.Close,
                    };
                    dialog.Show();
                    return Result.Cancelled;
                }

                ConnectionSettings settings = SignInGate.EnsurePassword(application, file);
                if (settings == null)
                    return Result.Cancelled;

                SignedInPerson person = SignInGate.Session.PersonFor(settings);
                return person == null ? Ask(application, settings) : OfferAnother(application, settings, person);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Ask(UIApplication application, ConnectionSettings settings) =>
            SignInGate.Ask(application, settings) == null ? Result.Cancelled : Result.Succeeded;

        private static Result OfferAnother(UIApplication application, ConnectionSettings settings, SignedInPerson person)
        {
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = SignInTexts.WhoTitle(person),
                MainContent = SignInTexts.WhoText(person),
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Sign in as someone else…", person.Name + " stays signed in until that works.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Sign out", "Nothing is checked until someone signs in again. The model keeps its values.");
            switch (dialog.Show())
            {
                case TaskDialogResult.CommandLink1:
                    return Ask(application, settings);
                case TaskDialogResult.CommandLink2:
                    SignInGate.Session.SignOut();
                    return Result.Succeeded;
                default:
                    return Result.Cancelled;
            }
        }
    }
}
