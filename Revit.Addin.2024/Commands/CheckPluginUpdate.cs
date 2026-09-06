using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Revit.Addin._2024.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CheckPluginUpdate : IExternalCommand
    {
        private static bool _isTeamsSynced = false;
        private const string SyncInstructions =
     "📁 Please follow these steps to sync the 31 BIM folder:\n\n" +
     "1️⃣  Microsoft Teams will open in your **browser**.\n" +
     "2️⃣  If prompted, sign in with your work account.\n" +
     "3️⃣  Click on the **'Open in SharePoint'** button at the top of the Files tab.\n" +
     "⬇️\n" +
     "4️⃣  In SharePoint, click the **'Synchronisieren'** (🔄 Sync) button in the toolbar.\n\n" +
     "🧩 You may be asked to allow OneDrive — confirm and allow it to complete the sync.\n\n" +
     "✅ Done! The folder will now sync to your computer.";

        public static bool ShouldRunUpdate { get; set; } = false;
        public static string UpdateScriptPath { get; private set; }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Resolve all version-specific paths from the running Revit version
            string revitVersion = commandData.Application.Application.VersionNumber;

            string serverDir = Environment.ExpandEnvironmentVariables(
                $@"%USERPROFILE%\Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\{revitVersion}\dependencies");
            string serverDllDir = Path.Combine(serverDir, "PluginTrail");
            string serverAddinPath = Path.Combine(serverDir, $"Revit.Addin.{revitVersion}.addin");
            string serverVersionFile = Path.Combine(serverDir, "version.txt");
            string localAddinDir = $@"C:\ProgramData\Autodesk\Revit\Addins\{revitVersion}";
            string localDllDir = Path.Combine(localAddinDir, "PluginTrail");
            string pluginDllName = $"Revit.Addin.{revitVersion}.dll";

            try
            {
                if (!_isTeamsSynced)
                {
                    TaskDialog TeamsSync = new TaskDialog("Teams Sync Check")
                    {
                        MainInstruction = "Have you synced the 31 BIM folder from OneDrive?",
                        MainContent = "If not, press Yes to open Microsoft Teams and follow the sync steps.\n\n" +
                                      "After syncing, re-run the 'Check for Updates' button to proceed.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                    };

                    if (TeamsSync.Show() == TaskDialogResult.Yes)
                    {
                        // Open Teams to allow user to sync
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://mhing.sharepoint.com/:f:/s/bim/EnndXLE_cLxKkeXBRY7emxwBcR5kh1hDzeC59c-hW66Q7g?e=GhLeaP",
                            UseShellExecute = true
                        });
                        TaskDialog.Show("Teams Sync", SyncInstructions);

                        _isTeamsSynced = true;
                        return Result.Succeeded;
                    }
                    else
                    {
                        TaskDialog.Show("Teams Sync", "Please ensure you have synced the Teams folder before proceeding.");
                        
                    }
                }


                if (!File.Exists(serverVersionFile))
                {
                    TaskDialog.Show("Update Check", "Cannot find version.txt on the server.");
                    return Result.Cancelled;
                }

                string serverVersion = File.ReadAllText(serverVersionFile).Trim();
                string localDllPath = Path.Combine(localDllDir, pluginDllName);

                if (!File.Exists(localDllPath))
                {
                    TaskDialog.Show("Update Check", "Local plugin DLL not found.");
                    return Result.Cancelled;
                }

                string localVersion = FileVersionInfo.GetVersionInfo(localDllPath).FileVersion;

                if (serverVersion == localVersion)
                {
                    TaskDialog.Show("Plugin Up to Date", $"You're already using version {localVersion}.");
                    return Result.Succeeded;
                }

                var dialog = new TaskDialog("Plugin Update Available")
                {
                    MainInstruction = $"Update from {localVersion} to {serverVersion}?",
                    MainContent = "Revit must be restarted. The update will install after you close Revit.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };

                if (dialog.Show() != TaskDialogResult.Yes)
                    return Result.Cancelled;
                ShouldRunUpdate = true;
                UpdateScriptPath = Path.Combine(Path.GetTempPath(), "run_plugin_update.bat");
                WriteUpdateScript(UpdateScriptPath, serverDllDir, serverAddinPath, localAddinDir, localDllDir);

                TaskDialog.Show("Update Scheduled", "Update will install after Revit is closed.");
                _isTeamsSynced = false;
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void WriteUpdateScript(string path, string serverDllDir, string serverAddinPath, string localAddinDir, string localDllDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine();
            sb.AppendLine("echo Updating Revit Plugin...");
            sb.AppendLine("echo Please wait, this may take a few moments...");
            sb.AppendLine("timeout /t 10 >nul");
            sb.AppendLine($@"set ""SERVER_DLL_DIR={serverDllDir}""");
            sb.AppendLine($@"set ""ADDIN_FILE={serverAddinPath}""");
            sb.AppendLine($@"set ""LOCAL_ADDIN_DIR={localAddinDir}""");
            sb.AppendLine($@"set ""LOCAL_DLL_DIR={localDllDir}""");
            sb.AppendLine();

            sb.AppendLine("echo SERVER_DLL_DIR: %SERVER_DLL_DIR%");
            sb.AppendLine("echo ADDIN_FILE: %ADDIN_FILE%");
            sb.AppendLine("echo LOCAL_ADDIN_DIR: %LOCAL_ADDIN_DIR%");
            sb.AppendLine("echo LOCAL_DLL_DIR: %LOCAL_DLL_DIR%");
            sb.AppendLine();

            sb.AppendLine("if not exist \"%LOCAL_DLL_DIR%\" mkdir \"%LOCAL_DLL_DIR%\"");
            sb.AppendLine();

            sb.AppendLine("xcopy /Y /Q /I \"%SERVER_DLL_DIR%\\*.dll\" \"%LOCAL_DLL_DIR%\\\"");

            sb.AppendLine("xcopy /Y /Q \"%ADDIN_FILE%\" \"%LOCAL_ADDIN_DIR%\\\"");
            sb.AppendLine();

            sb.AppendLine("echo Update Complete.");
            sb.AppendLine("pause");
            sb.AppendLine("exit");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}