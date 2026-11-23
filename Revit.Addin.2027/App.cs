using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using MH.RevitTools.Database.Linking;
using Revit.Addin._2027.Commands;
using Revit.Addin._2027.DatabaseLink;
using Revit.Addin._2027.DatabaseLink.Pane;
using Revit.Addin._2027.Updaters;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Revit.Addin._2027
{
    public class App : IExternalApplication
    {
        // This class implements the IExternalApplication interface,
        // which is the entry point for Revit add-ins
        // OnStartup is called when the add-in is loaded
        public Result OnStartup(UIControlledApplication application)
        {
            application.ApplicationClosing += OnRevitClosing;

            // Subscribe for the DialogBoxShowing event
            application.DialogBoxShowing += Application_DialogBoxShowing; // the handler is not used

            // ---------------------------------------------------------------
            // Ribbon setup. Panels are grouped by functionality:
            //   • Database             — the live link from the MH database
            //   • Parameter Automation — seed parameter data into the model
            //   • Tabellen             — generate output tables
            //   • Functionality        — model utilities + plugin maintenance
            // ---------------------------------------------------------------
            string tabName = "Parameter Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            var panelDatabase = application.CreateRibbonPanel(tabName, "Database");
            var panelParameters = application.CreateRibbonPanel(tabName, "Parameter Automation");
            var panelTables = application.CreateRibbonPanel(tabName, "Tabellen");
            var panelFunctionality = application.CreateRibbonPanel(tabName, "Functionality");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;

            // ---- Database -------------------------------------------------
            // Laid out like the mockup: the big Database button, then three rows: Apply changes, Check
            // parameters, and the signed-in person. Database shows and hides the MH Database pane; it shows
            // the active model's project number, and a dot with what the pane's check found. Apply changes
            // applies what the pane lists. Sign in (step 8) stays greyed out until it's built. Test connection
            // sits in the panel's slide-out, under the panel's arrow.
            PushButton database = AddDatabasePanel(panelDatabase, assemblyPath, assemblyName);
            _databaseButton = new DatabaseButton(database);
            _databaseButton.Hook(application);
            StartDatabasePane(application);

            // ---- Parameter Automation -------------------------------------
            var btnAddParams = new PushButtonData(
                "AddParams",
                "Add Parameters\nto Families",
                assemblyPath,
                "Revit.Addin._2027.Commands.AddParametersToFamilies")
            {
                ToolTip = "Fügt den Familien/Elementen die benötigten gemeinsamen Parameter hinzu "
                          + "und schreibt die Werte aus der Excel-Liste (nach Bauteilnummer).",
                Image = LoadIcon(assemblyName, 32, "ParameterAutomationIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "ParameterAutomationIcon.png"),
            };
            panelParameters.AddItem(btnAddParams);

            var btnMaterial = new PushButtonData(
                "MaterialAutomation",
                "Automate Material\nCreation",
                assemblyPath,
                "Revit.Addin._2027.Commands.MaterialAutomationCommand")
            {
                ToolTip = "Erstellt/bindet Materialparameter und überträgt die Materialdaten "
                          + "aus der Excel-Liste auf die Projektmaterialien.",
                Image = LoadIcon(assemblyName, 32, "AddMaterialParametersRE.png"),
                LargeImage = LoadIcon(assemblyName, 32, "AddMaterialParametersRE.png"),
            };
            panelParameters.AddItem(btnMaterial);

            // ---- Tabellen -------------------------------------------------
            var btnTable = new PushButtonData(
                "GenerateTable",
                "Tabelle\nerstellen",
                assemblyPath,
                "Revit.Addin._2027.Tables.Commands.GenerateTableCommand")
            {
                ToolTip = "Erzeugt eine Tabelle (z.B. Einbauteile) aus Elementen der aktiven Ansicht.",
                LongDescription = "Liest Bauteilgruppe und feste Tabellenparameter aus den Modellelementen "
                                + "der aktiven Ansicht, gruppiert/sortiert sie und gibt das Ergebnis als "
                                + "Plansicht in Revit, Excel-Datei oder PDF aus.",
                Image = LoadIcon(assemblyName, 32, "Tables_Icon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "Tables_Icon.png"),
            };
            panelTables.AddItem(btnTable);

            // ---- Functionality --------------------------------------------
            var btnHide = new PushButtonData(
                "ShowByID",
                "Hide Elements",
                assemblyPath,
                "Revit.Addin._2027.Commands.ShowByUniqueID")
            {
                ToolTip = "Blendet Elemente nach Bauteilnummer ein/aus oder isoliert sie.",
                Image = LoadIcon(assemblyName, 32, "HideElementsIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "HideElementsIcon.png"),
            };
            panelFunctionality.AddItem(btnHide);

            var btnFamilyUpdater = new PushButtonData(
                "FamilyFileUpdater",
                "Update Family Files",
                assemblyPath,
                "Revit.Addin._2027.Commands.FamilyFileUpdater")
            {
                ToolTip = "Aktualisiert .rfa-Familiendateien stapelweise in einem Ordner.",
                LongDescription = "Durchsucht einen Ordner (optional mit Unterordnern) nach .rfa-Dateien "
                                + "und wendet die ausgewählten Schritte an. Wahlweise werden die Originale "
                                + "überschrieben (mit Sicherungskopie) oder die aktualisierten Kopien in "
                                + "einen zweiten Ordner geschrieben. Erfordert kein geöffnetes Projekt.",
                Image = LoadIcon(assemblyName, 32, "FamilyFileUpdate.png"),
                LargeImage = LoadIcon(assemblyName, 32, "FamilyFileUpdate.png"),
            };
            panelFunctionality.AddItem(btnFamilyUpdater);

            var btnCheckUpdate = new PushButtonData(
                "CheckUpdate",
                "Check for Updates",
                assemblyPath,
                "Revit.Addin._2027.Commands.CheckPluginUpdate")
            {
                ToolTip = "Prüft den zentralen Server auf eine neuere Plugin-Version.",
                Image = LoadIcon(assemblyName, 32, "Update_plugin.png"),
                LargeImage = LoadIcon(assemblyName, 32, "Update_plugin.png"),
            };
            panelFunctionality.AddItem(btnCheckUpdate);

            return Result.Succeeded;
        }

        private const string Commands = "Revit.Addin._2027.Commands.";

        // Keeps the big Database button showing the active model's link.
        private DatabaseButton _databaseButton;

        // Runs the MH Database pane: the check, the comparison and what the pane shows.
        private LinkMonitor _linkMonitor;

        // The MH Database pane (step 5). A pane that can't start must not take the tab down with it: the
        // Database button then says why.
        private void StartDatabasePane(UIControlledApplication application)
        {
            try
            {
                _linkMonitor = new LinkMonitor(_databaseButton, Dispatcher.CurrentDispatcher);
                _linkMonitor.Hook(application);
                DatabasePane.Register(application, new PaneView { DataContext = _linkMonitor.View });
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[Parameter Tools] The MH Database pane didn't start: " + ex);
                DatabasePane.StartProblem = ex.Message;
                if (_linkMonitor != null)
                {
                    _linkMonitor.Unhook(application);
                    _linkMonitor.Dispose();
                    _linkMonitor = null;
                }
            }
        }

        /// <returns>The big Database button, whose text and icon follow the active model's link.</returns>
        private static PushButton AddDatabasePanel(RibbonPanel panel, string assemblyPath, string assemblyName)
        {
            var database = (PushButton)panel.AddItem(new PushButtonData("DatabaseLink", DatabaseButtonFace.NoModel.Text, assemblyPath, Commands + "DatabasePaneCommand")
            {
                ToolTip = DatabaseButtonFace.NoModel.ToolTip,
                AvailabilityClassName = Commands + "AvailableInProject",
                Image = LoadIcon(assemblyName, 16, "DatabaseLinkIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "DatabaseLinkIcon.png"),
            });

            var applyChanges = new PushButtonData("ApplyDatabaseChanges", "Apply changes", assemblyPath, Commands + "ApplyChangesCommand")
            {
                ToolTip = "Applies the MH database's changes to this model, after asking, in one undo step. The MH "
                          + "Database pane lists them and can apply them one by one.",
                AvailabilityClassName = Commands + "AvailableInProject",
                Image = LoadIcon(assemblyName, 16, "ApplyChangesIcon.png"),
            };
            var checkParameters = new PushButtonData("CheckDatabaseParameters", "Check parameters", assemblyPath, Commands + "CheckParametersCommand")
            {
                ToolTip = "Lists the MH database's parameters in this model, adds the missing ones from the master "
                          + "shared parameter file, and can replace those of the same name with another GUID.",
                AvailabilityClassName = Commands + "AvailableInProject",
                Image = LoadIcon(assemblyName, 16, "CheckParametersIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "CheckParametersIcon.png"),
            };
            var signIn = new PushButtonData("SignInDatabase", "Sign in…", assemblyPath, Commands + "NotYetCommand")
            {
                ToolTip = "Signs you in with your MH Database Studio account; then it shows your name. Comes with "
                          + "step 8 of the database link.",
                AvailabilityClassName = Commands + "NotYetAvailable",
                Image = LoadIcon(assemblyName, 16, "SignInIcon.png"),
            };
            panel.AddStackedItems(applyChanges, checkParameters, signIn);

            panel.AddSlideOut();
            panel.AddItem(new PushButtonData("TestDatabaseConnection", "Test\nconnection", assemblyPath, Commands + "TestConnectionCommand")
            {
                ToolTip = "Checks that Revit can reach the MH database with this PC's connection file.",
                AvailabilityClassName = Commands + "AvailableWithoutModel",
                Image = LoadIcon(assemblyName, 32, "DatabaseIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "DatabaseIcon.png"),
            });
            return database;
        }

        /// <summary>
        /// Loads a ribbon icon from the embedded ModernIcons resources at the requested size
        /// (16/32/64). LargeImage uses 64 px and the small Image uses 32 px so both render
        /// crisply across Revit's DPI scaling.
        /// </summary>
        private static BitmapImage LoadIcon(string assemblyName, int size, string fileName)
        {
            try
            {
                return new BitmapImage(new Uri(
                    $"pack://application:,,,/{assemblyName};component/Icons/ModernIcons/{size}/{fileName}"));
            }
            catch (System.IO.IOException ex)
            {
                // A missing icon must not take the whole Parameter Tools tab down: the button goes without it.
                Debug.WriteLine("[Parameter Tools] No icon " + size + "/" + fileName + ": " + ex.Message);
                return null;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_linkMonitor != null)
            {
                _linkMonitor.Unhook(application);
                _linkMonitor.Dispose();
            }
            _databaseButton?.Unhook(application);
            application.DialogBoxShowing -= Application_DialogBoxShowing; // Unsubscribe from the dialog box showing event
            return Result.Succeeded;
        }
        // This method handles the dialog box showing event to override the result of a specific dialog
        // In this case, it overrides the result of the "TheSuccess" dialog to always return Ok
        // This is just a placeholder to test how to handle dialog events
        private void Application_DialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            if (e is TaskDialogShowingEventArgs args && args.DialogId == "TheSuccess")
            {
                args.OverrideResult((int)TaskDialogResult.Ok);
            }
        }

        // This method is obsolete and not used in Revit 2024 due to API changes
        [AvailableFromVersion("2026")]
        private void Populate_NewElementsList(object sender, DocumentChangedEventArgs args)
        {
            Debug.WriteLine("DocumentChanged triggered");

            var added = args.GetAddedElementIds();
            var modified = args.GetModifiedElementIds();

            Debug.WriteLine($"ADDED: {added.Count}, MODIFIED: {modified.Count}");

            foreach (ElementId id in added)
            {
                Debug.WriteLine($"New element: {id.Value}");
                UpdateNewlyAdded.newElementIds.Add(id);
            }
        }
        private void OnRevitClosing(object sender, ApplicationClosingEventArgs e)
        {
            if (CheckPluginUpdate.ShouldRunUpdate && !string.IsNullOrWhiteSpace(CheckPluginUpdate.UpdateScriptPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = CheckPluginUpdate.UpdateScriptPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                });
            }
        }


        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        public class AvailableFromVersionAttribute : Attribute
        {
            public string Version { get; }

            public AvailableFromVersionAttribute(string version)
            {
                Version = version;
            }
        }

    }
}
