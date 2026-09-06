using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Revit.Addin._2027.Commands;
using Revit.Addin._2027.Updaters;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;

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
            //   • Parameter Automation — seed parameter data into the model
            //   • Tabellen             — generate output tables
            //   • Functionality        — model utilities + plugin maintenance
            // ---------------------------------------------------------------
            string tabName = "Parameter Tools";

            try { application.CreateRibbonTab(tabName); } catch { }

            var panelParameters = application.CreateRibbonPanel(tabName, "Parameter Automation");
            var panelTables = application.CreateRibbonPanel(tabName, "Tabellen");
            var panelFunctionality = application.CreateRibbonPanel(tabName, "Functionality");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;

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

            var btnPopulateDb = new PushButtonData(
                "PopulateDB",
                "Populate DB\nParameters",
                assemblyPath,
                "Revit.Addin._2027.Commands.PopulateFromDbCommand")
            {
                ToolTip = "Füllt Elementparameter aus der MH-Datenbank (Auswahl nach Projekt).",
                Image = LoadIcon(assemblyName, 32, "PopulateDbIcon.png"),
                LargeImage = LoadIcon(assemblyName, 32, "PopulateDbIcon.png"),
            };
            panelParameters.AddItem(btnPopulateDb);

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
            ShowStartupBanner(assemblyPath);

            return Result.Succeeded;
        }

        /// <summary>
        /// Startup confirmation, shown once when Revit loads the add-in.
        /// <para>
        /// Deliberately reports the folder the assembly was actually loaded from, because that is
        /// the thing worth verifying after the payload folder was renamed from PluginTrail to
        /// MH.RevitTools: if the manifest and the install disagree, the add-in does not load at all
        /// and this dialog never appears. Seeing it, with MH.RevitTools in the path, proves the
        /// manifest, the install location and the deployed build all line up.
        /// </para>
        /// <para>
        /// This is a visible marker for testing a release. Remove it once the rename is confirmed
        /// in the office - a modal dialog on every Revit start gets old quickly.
        /// </para>
        /// </summary>
        private static void ShowStartupBanner(string assemblyPath)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion;
                var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(assemblyPath));

                var dialog = new TaskDialog("MH Revit Tools")
                {
                    MainInstruction = $"MH Revit Tools {version} loaded",
                    MainContent =
                        $"Loaded from:  {folder}\n\n" +
                        $"Full path:\n{assemblyPath}\n\n" +
                        "If this says MH.RevitTools, the renamed payload folder is working.",
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                dialog.Show();
            }
            catch
            {
                // A banner must never stop the add-in loading.
            }
        }

        /// <summary>
        /// Loads a ribbon icon from the embedded ModernIcons resources at the requested size
        /// (16/32/64). LargeImage uses 64 px and the small Image uses 32 px so both render
        /// crisply across Revit's DPI scaling.
        /// </summary>
        private static BitmapImage LoadIcon(string assemblyName, int size, string fileName)
        {
            return new BitmapImage(new Uri(
                $"pack://application:,,,/{assemblyName};component/Icons/ModernIcons/{size}/{fileName}"));
        }

        public Result OnShutdown(UIControlledApplication application)
        {
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
