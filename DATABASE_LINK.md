# Database link: MH database → Revit 2027

**Status (2026-11-19):** steps 1 and 2 are done and committed on the branch `database-link` (pushed;
master is untouched). The add-in reads `revit` views, which build the IDs like Studio and take the
change stamp from the history. The database session has built its side: the views, each parameter's
GUID and Revit name, and the master shared parameter file. The views are in dev MH_Database. Its
answers are under "What the database session built". Step 3 (link the model to its project) is done
and committed (ee72e57). Step 4 (Check parameters) is done: Yehia walked it on 2026-11-22. At his
request it gained Replace for Other GUID parameters, and the Database panel now has the mockup's layout
(00b0fb7, pushed). After walking those, he asked for two more: the Database button shows the linked
project's number with a green dot (walked, works), and Replace also takes parameters that schedules or
view filters use, after a second warning. In his model Revit would delete other elements with every
parameter, so Replace now lists those elements, says where each one is and can show them in a 3D view of
their own, and deletes them only after a confirmation. Yehia's answer: "everything is good" (da37289,
pushed). In Yehia's model none of those elements is one a view can show, so "Show them in a new view"
couldn't be tried. Step 5 (the MH Database pane and the change check) is done: built, committed (1721f93)
and walked on 2026-11-23. Step 6 (Apply and "Not applied") is done: built, committed and walked on
2026-11-23. Step 7 (Compare with Excel) is done: built, committed and walked on 2026-11-23. Step 8 (Sign in) is
done: built, committed and walked on 2026-11-23. **Step 9 (publish it for colleagues) is under way: the
connection file is built into the add-in, and Revit asks each person once for the database password (Yehia, 2026-11-24).
The add-in's half is built and committed; Yehia walks it once the office can be reached. The database session's half is
prepared; CADSQL can't be reached for now, so the login isn't made yet.** Step 10 (create missing materials, set fill patterns)
is done: built, committed and walked on 2026-11-25. Step 11 (assign materials) is done: built, walked and committed on 2026-11-25.
**Step 12 (fill the material's own fields) is planned; it needs more rows from the database session.** Version 5.3.0.0 was released on
2026-11-25 without the link; 5.3.1.0, with four fixes of the Excel import, is to follow the same way.

The live link from the MH database into Revit, inside the "Parameter Tools" add-in (MH.RevitTools),
in `Revit.Addin.2027` only. It only reads from the database. This file holds the plan, the status, and
what we need from the database session ("Needs from the database" below).

The full brief (what's decided, the rules) came with the hand-off on 2026-11-19. The decisions made
after it are listed next.

## Decided on 2026-11-19

- **Sign-in.** The add-in connects with one read-only SQL login, through a database role that can only
  read the `revit` views. Its password sits in a connection file that the installer puts on each PC.
  Each person then signs in with their MH Database Studio account each time Revit starts. No AD group.
  - The Studio check never reads the `Users` table. Two small procedures do it instead (see "Needs
    from the database"). Otherwise anyone holding the connection file could copy every Studio password
    hash, TheAdmin's included.
  - The file lives at `C:\ProgramData\MH\RevitTools\database.json`, not in the add-in folder. That gives
    one file per PC for every Revit year. Add-in updates never touch it, and it can never end up in the
    build output or on the share. Its format is under "The connection file" below. (Not for now: see the
    decisions of 2026-11-24.)
  - Every active Studio account may sign in. There's no role check.
  - Encrypting the password for the PC (Windows DPAPI, machine scope) may come later. For now the file
    holds it as plain text, so anyone who can open the file on that PC can read it. (Not any more: see the
    decisions of 2026-11-24.)
- **Project suggestion.** Models write the Project Number (Project Information) as `1761` without a
  sub-number and `1761-01` with one. The first 4 digits are the project number.
  - With a sub-number (`-01` means 1), the add-in suggests the project with that number and sub-number.
  - Without one, it suggests the project if it's the only one with that number. Otherwise the dialog
    lists all of them and the user picks.

  It's only ever a suggestion: the user confirms it in the Link dialog. The dialogs write projects the
  same way (`1761`, `1761-01`).
- **The old "Populate DB Parameters" prototype is deleted** from 2027: the button,
  `Commands\PopulateFromDbCommand.cs`, `DB\DbRepo.cs`, `DB\FamilySharedParamWriter.cs`,
  `UI\ProjectPickerForm.cs` and `PopulateDbIcon.png`. 2024 and 2026 keep theirs.
- **Test data.** `C:\Yehia\DatabaseFiles\Planungsdatenbank.xlsx` matches project 1761 Ulm. No test model
  carries Bauteil_IDs yet. Yehia adds a few before step 6.
- **Test packages approved:** xunit 2.9.2, xunit.runner.visualstudio 2.8.2, Microsoft.NET.Test.Sdk
  17.11.1 (the same as Database.Data.Tests).
- **Testing against the local MH_Database is fine** (Yehia, with step 2). The tests only read it.
- **The link work lives on the branch `database-link`** until Yehia merges it. Master stays releasable.
- **`revit` views after all** (Yehia, step 2). He first ruled them out, then asked for them on my
  recommendation, so that Studio can change its tables without breaking Revit and the shared login sees
  only what Revit needs. The add-in team (this session) writes the view SQL (`RevitViews.sql`, tested
  here); the database session puts it into MH_Database through a Studio migration.
- **Bauteil-ID and Material-ID are built at lookup** the way Studio builds them, never read from a
  stored copy (Yehia, step 2).
- **Studio's change history (AuditLogs) drives the change check** and says who changed a value and when
  (Yehia suggested it in step 2).
- **Duplicate Bauteil-IDs stay as they are** until Yehia gives the fix (a new Bauteilunternummer).
- **The link fills Bauteilnummer and Bauteilunternummer** (Yehia, 2026-11-23, step 6): the modellers enter the
  Bauteil_ID by hand, and that's the unique ID. Only the Bauteil_ID itself is never written.
- **What the views don't give stays unfilled for now** (Yehia, 2026-11-23, step 7): the 14 material parameters,
  the two object ones and the global materials under "Needs from the database", item 5.
- **Materials on objects** (Yehia, 2026-11-23: "Yes, plan it"): the link is to assign materials to objects, as the
  Excel import does. Planned as steps 10 and 11.
- **Parameter definitions come from Studio's master file** (Yehia, 2026-11-20, step 4), as in the
  mockup. The connection file names the file, so each PC pairs a database with its own file, and the
  add-in refuses a file that holds none of the database's GUIDs.
- **For now without the lock and the hand-over** (Yehia, 2026-11-24). Yehia is in Egypt and the office is in Germany; the
  link is to be published so that colleagues can install it themselves.
  - Lifted: the connection file handed over by stick, administrator rights to install it, the locked folder. It goes along with
    the add-in instead (step 9).
  - Kept, as it's in nobody's way: a login of its own that can only read the `revit` views and call the sign-in procedures,
    never Studio's; the sign-in with the Studio account; the encrypted connection; no password in Git, chat or logs.
  - The installer that locks the file is built (`Scripts\ConnectionFile`), but not used: see the next decision. It stays in the
    repo (Yehia: "Leave it for now").
- **Each person enters the database password once** (Yehia, 2026-11-24). Yehia's idea: the connection file ships without it, and
  Yehia sends it to each colleague in a private Teams message. Revit asks for it in a window and keeps it encrypted, instead of
  colleagues editing the file (this session's proposal; Yehia: "Good go with your suggestion then"). So the package, the ZIP and
  the Teams folder hold nothing secret.
- **CADSQL can't be reached for now** (Yehia, 2026-11-24): the database session prepares everything it can until it can be again.
- **Revit uses the connection file that ships with the add-in** (Yehia, 2026-11-24: "we don't want a global file that everyone can
  access through share"). It's built into the add-in's DLL (this session's way): Check for Updates of 5.2.2.0 copies DLLs only, so
  a separate file beside the add-in wouldn't reach whoever updates with it. A PC's own file still wins, as Yehia's PC needs it
  for dev; colleagues have none.

## The connection file

`database.json`. The add-in only reads it, at every check. Which one Revit 2027 uses:

1. `C:\ProgramData\MH\RevitTools\database.json`: the PC's own, for every Revit year, as on a developer's PC. If it's there, it's
   the only one that counts, good or broken: a mistake in a developer's file must never lead to the office's database.
   Colleagues have none.
2. Otherwise the one built into the add-in (`Revit.Addin.2027.dll`). It comes with every version, whichever way that reaches the
   PC, and it's always the one of the version that runs. Nothing lies on the PC or on the share to open or change.
   Check for Updates of 5.2.2.0 wouldn't bring a separate file: it copies only DLLs, and it's the version before that does
   the copying.

To change it takes a new version of the add-in; for one PC, a file of its own.

The office's is `Revit.Addin.2027\database.json` in the repo. Whoever has the add-in could read it, so it never holds a
password; tests see to that, to its six values, and that it's built in. `.gitignore` leaves out any other `database.json`. The
database session confirmed the six values on 2026-11-24:

```json
{
  "server": "CADSQL.mh-frlg.local,1433",
  "database": "MH_Database",
  "login": "revit_reader",
  "encrypt": true,
  "trustServerCertificate": true,
  "masterParameterFile": "\\\\CADSQL.mh-frlg.local\\PLDB\\01.Datenbank\\MH_Parameter.txt"
}
```

On Yehia's PC for development (LocalDB, Windows sign-in, no password):

```json
{
  "server": "(localdb)\\MSSQLLocalDB",
  "database": "MH_Database",
  "windowsSignIn": true,
  "masterParameterFile": "%LOCALAPPDATA%\\MH Database Studio\\Development share\\PLDB\\01.Datenbank\\MH_Parameter.txt"
}
```

- `masterParameterFile` is the database's master shared parameter file, the one Studio writes for this
  database. Environment variables such as `%LOCALAPPDATA%` are filled in for the user running Revit.
  Without it, Check parameters lists but can't add.

- `encrypt` and `trustServerCertificate` are what Studio's connection to the office does today (the database
  session, 2026-11-24). `"trustServerCertificate": true` means the server's certificate
  isn't checked, which puts the shared password at risk on the network. A certificate the PCs trust
  avoids that. If the first contact with CADSQL shows that Yehia's PC trusts its certificate, Yehia says so; only then
  is `false` worth trying, and before the package is published.
- This file doesn't hold anyone's own settings:
  - how often to check and auto-apply: `%APPDATA%\MH\RevitTools\settings.json`
  - the last pull: `%LOCALAPPDATA%\MH\RevitTools\`
- **The password.** A file may hold one (`"password"`), as a PC's own file does if it has a login. Without one, Revit asks
  for it and keeps it in `%LOCALAPPDATA%\MH\RevitTools\Passwords`, encrypted by Windows for that person on that PC (DPAPI).
  There's one per server, database and login, and only that connection gets it: a file changed to name another server
  doesn't.
  - It's never written into a connection file, and an update of the add-in doesn't touch it. So each person enters it once per
    PC, and not again with an update.
  - Revit asks again when the server refuses it (there's a new one), on another PC or Windows account, when a new version
    names another server, database or login, and when Windows can't read it any more (the profile was made anew, or an
    administrator reset the Windows password of a local account).

## How the code is split

- **`MH.RevitTools.Database`** (new, no Revit). It holds:
  - the connection file
  - the reads of the `revit` views and the change check
  - the cache of the last pull
  - the project-number rule and what the Link dialog says (`Linking`)
  - the Studio password hashing
  - the database passwords kept on the PC (`SavedPasswords`; Windows' encryption comes with SqlClient's own packages)

  It targets net48 and net10.0, so 2024 and 2028 only need to add a reference. It uses only packages
  already in the repo (Microsoft.Data.SqlClient 5.2.2, Newtonsoft.Json 13.0.3). All database access
  goes through one interface, with the real SQL version and a fake.
- **`MH.RevitTools.Database.Tests`** (new): xUnit. It also holds the views' SQL as handed over,
  `Link/RevitViews.sql`. Studio's migration is now the one source of the views, and this copy follows it
  for the tests. The query tests run:
  - over stand-in tables with known rows, in throwaway LocalDB databases of their own, dropped
    afterwards (`Category=LocalDB`);
  - over the real views in dev MH_Database (`Category=LocalMhDatabase`). These only read it.
- **`Revit.Addin.2027`**: the Database panel, the pane and the dialogs (WPF, in the mockup's look). It
  turns database rows into the same `PlanungsdatenbankParseResult` the Excel reader produces, and applies
  values through an ExternalEvent.
- **Reused, not rewritten:** `ElementKey` (matching by Bauteil_ID), `ParamMetaData.ConvertValue` (invariant
  parsing and unit conversion, already used for the Excel's Einheit). The placeholder rule has a Revit-free
  copy in the library (`Placeholders`), as the library can't see `PlanungsdatenbankLayout.IsSkipped`: change
  both together.
- **One refactor (step 6).** The value-writing loop sat inside `SeedProjectElementsParameters.AddParametersToElements`,
  which also adds parameters to families. The link must never do that, so the loop is now in
  `Helpers\ElementValueWriter`, and Excel and database both call it, each with its own rules. The Excel
  path behaves exactly as before.
- **The rules.** The Excel path finds parameters by name; the database by GUID (`ParamMetaData.SharedGuid`),
  on the element itself only. The database's values take the shape the Excel reader produces
  (`PlanungsdatenbankParseResult`, made by `DatabaseParseResult`). The pane's list and Apply ask the same
  class how a value compares and what Revit gets (`RevitValues`), so what's listed is what's written. What's
  skipped is collected while comparing, which is what "Not applied" needs.

## Steps

The riskiest come first. Each step ends with a walkthrough in Revit 2027, and the next step starts only
after Yehia has walked it.

### 1. Spike: does SqlClient work inside Revit 2027?
- A new **Database** panel on Parameter Tools. For now it has one button, **Test connection**; later
  that button opens the pane.
- The button reads the connection file. On Yehia's PC there is a dev file pointing at LocalDB with
  Windows sign-in, so it has no password; this is for dev only. The button shows the server, the
  database, who you're signed in as, the SQL Server version and the time taken, or the error in plain
  words. It runs one read-only query. Connecting and the query each give up after 5 seconds.
- The old Populate DB button and its code are removed.
- The two new projects are created, with the connection-file reader under tests.
- **Built on 2026-11-19.** What the build turned up:
  - **The bug the spike was for.** The released 5.2.2.0 add-in folder has SqlClient's platform-neutral
    placeholder in its root. The real Windows build sits only under `runtimes\`, which a host finds only
    by reading the add-in's deps.json. Loaded straight from the folder, the placeholder throws "not
    supported on this platform". The 2027 project now builds for win-x64 (`RuntimeIdentifier`), so the
    Windows `Microsoft.Data.SqlClient.dll`, its native `Microsoft.Data.SqlClient.SNI.dll` and
    `System.Runtime.Caching.dll` sit in the folder root. That also suits "Check for Updates", which
    copied only the top-level `*.dll` then. Old `runtimes\` folders stay in installed add-in folders, because
    the deploy doesn't wipe; they're harmless.
  - **Checked outside Revit.** A throwaway loader loaded the deployed folder the way a host loads a
    plug-in (LoadFrom, no deps.json, .NET 10 desktop runtime). It connected in 0.2 s. With the old layout
    it fails with the placeholder message.
  - **Answered by the walk.** Revit 2027's own folder ships `System.Configuration.ConfigurationManager`
    7.0, and SqlClient needs 8.0 or later. The walk connected, so Revit loads the .NET desktop
    runtime's 10.0.
  - LocalDB connects with encryption on and the certificate checked, so the dev file needs no
    `trustServerCertificate`.
  - The dev connection file is in place on Yehia's PC.
  - The button also works with no model open.
  - C# and security reviews found nothing critical or high. Fixed after them:
    - the test no longer reuses a pooled session;
    - the certificate message no longer suggests switching the check off;
    - two keys that differ only in case are an error;
    - connection files over 64 KB are refused.

    The details now also say how the connection is encrypted.
- **Walk:**
  1. Close Revit; I build. (Done.)
  2. Open Revit 2027 → Parameter Tools → Database (the first panel) → Test connection. No model needs
     to be open.
  3. You should see "Connected", then "MH_Database on (localdb)\MSSQLLocalDB", "Signed in as" your
     Windows account, and "SQL Server 2019 (15.0.4382.1) · 0.2 s". "Show details" names the driver files
     Revit loaded; all of them should be in `C:\ProgramData\Autodesk\Revit\Addins\2027\MH.RevitTools`.
  4. Rename the file and click again: the message names the missing file. Rename it back.
  5. Check that the Populate DB button is gone and that the other buttons and MH.IfcCustomExport still work.

### 2. The contract and the data layer
- "Needs from the database" (below) is finished, so the database session can start on it.
- The data layer reads the `revit` views. Their SQL is written and tested here, and handed over as
  `RevitViews.sql`.
- **Built on 2026-11-19, in three rounds:** draft views, then direct queries (Yehia's first answer), then
  views again (his final choice).
  - `RevitViews.sql`:
    - builds Bauteil-IDs and Material-IDs like Studio;
    - maps RevitCategory to Revit's category names;
    - takes the change stamp (history row count plus newest id) and who/when from AuditLogs, never
      its Changes column.
  - `ILinkDatabase` in `MH.RevitTools.Database`:
    - `SqlLinkDatabase` reads only the views.
    - Each project is read in one batch, with the stamp read first and last. If the stamp moved, or SQL
      Server picked the read as a deadlock victim, it reads again (up to 3 times).
    - `FakeLinkDatabase` is an in-memory version for tests of what comes next.
  - 106 tests pass:
    - the views over stand-in tables, and over 1761 Ulm in the local MH_Database;
    - the views as a user who has only the `revit` grant;
    - the script run twice.
  - Three reviews (C#, security, then the table version). Their points are in:
    - the views: commit-order-safe stamp, index-friendly history lookup, empty codes as in Studio, UTC
      times;
    - the request: the schema-only grant, the certificate, the sign-in lockout, and auditing material
      categories and groups.
- Revit reads the views from step 3 on, once they exist in dev MH_Database. Until the dictionary has
  them, the GUIDs are empty and Revit shows the plain names.
- **Walk:** read "Needs from the database" and pass it on. Nothing to click in Revit.

### 3. Link the model to its project
- The MH Database pane's **Link the model…** and **Change project…** open the dialog from the mockup
  (until step 5 the big Database button did, and before 2026-11-22 a Link project button). It reads the
  projects in the background and suggests one from the Project Number (rule above).
- Link saves the project in the model: an Extensible Storage entity on Project Information (schema
  `MH_ProjectLink`). Any add-in can read it; only ours can change it.
  - The entity keeps the project's id, its number, sub-number and name, who linked it and when.
  - So the dialog can tell when the id has come to mean another project and asks again. That happens
    with another database, where the ids differ, or after a renumbering in Studio.
- The button is greyed out without a project model, and in the family editor. In a workshared model,
  the add-in borrows Project Information first. If someone else is editing it, or the model is behind
  central, a plain message says so instead of linking.
- A copy made with Save As keeps the link, like everything else in Project Information. Link the copy
  again if it belongs to another project.
- **The big Database button shows the active model's link** (Yehia, 2026-11-22): "Database" over the
  project's number (1761-01) once the model is linked, with a dot on the icon (since step 5 it shows the
  pane's state; until then it was green as soon as the model was linked); "Database" over "Link model"
  while it isn't; plain "Database" without a project model. The tooltip names the project, who linked it
  and when. The button follows the active view from model to model, Link project, Undo and
  Redo, and what Synchronize with Central or Reload Latest brings. It reads only the model, never the
  database.
- **Walk:**
  1. Open a test model whose Project Number is 1761-01 → Database → Link project. The dialog says
     "Suggested from Project Information: Project Number 1761-01 matches 1761-01 Ulm", with Ulm picked.
  2. Click "Link to 1761-01 Ulm". You get "Linked to 1761-01 Ulm. Save the model to keep the link."
  3. Save, close and reopen the model, then Link project again. It says "… is linked to 1761-01 Ulm.
     Linked by (your Revit user name), (time)", and the button says "Already linked".
  4. Optional, on a model that isn't linked: with the Project Number 1761, Ulm is suggested too, as dev
     has only one 1761. With no Project Number, the dialog asks you to pick.
  5. The button (added 2026-11-22): a linked model shows "Database / 1761-01" with the green dot; an
     unlinked one "Database / Link model". Link a model: the button changes at once; Ctrl+Z puts it
     back. With two models open, switching between their views switches the button.

### 4. Check parameters
- Database → **Check parameters** opens the dialog from the mockup. It lists every database parameter
  on each of its categories (dev: 91 on Generic Models, 26 on Materials) against the model's project
  parameters. Each one is found by its GUID:
  - **Ready**: the model has it.
  - **Missing**: Add can bind it.
  - **Can't add**: the note says why, e.g. Studio hasn't written it into the master file yet.
  - **Other GUID**: the model has a parameter of that name on that category with another GUID, or
    one that isn't shared. The link leaves it empty unless it's replaced (below).
- **Replace N…** (Yehia, 2026-11-22), in the red Other GUID box. Revit can't change a parameter's GUID,
  so replacing deletes the model's parameter, with its values in every element and on every category,
  and binds the database's in its place.
  - It asks first, naming the parameters ("Replace them" / "Keep them"; Cancel is the default). The
    question also says that tags show the new parameters only once their tag families use the master
    file's.
  - All swaps are one undo step. Each swap is all or nothing: if the database's parameter can't be
    bound, the old one stays, values and all.
  - **When schedules or view filters use some of them, it asks once more** (Yehia, 2026-11-22; until
    then those were left alone). The warning names each parameter's schedules and view filters, and
    says what replacing does there: the schedules lose its column, and the filters can no longer
    filter by it, so the new parameter goes into them by hand afterwards. The answers are "Replace all
    N" / "Replace only the other M", or "Replace them anyway" / "Keep them" when all are in use; Cancel
    is the default. The add-in looks in schedule fields (combined and embedded ones too) and in view
    filter rules. When Revit can't tell, every parameter counts as used.
  - **When Revit can only delete a parameter together with other elements, it lists them first**
    (Yehia, 2026-11-22: until then such a swap was refused, and in his model every one was). The
    add-in finds them by deleting each parameter in a change it always undoes, then shows a list:
    each element's name, kind, id (for Select by ID) and the parameter it goes with. "Delete N
    elements and replace" deletes them and replaces; "Replace only the other N" leaves those
    parameters; Cancel does nothing. Replace never deletes an element that wasn't in the list: a swap
    that would is refused.
  - Under each name the list says where the element is: "In schedule “…”" for one that belongs to a
    view, "Not shown in any view" for Revit's own records. A nameless element shows its type's name if
    it has one. **"Show them in a new view"** (Yehia, 2026-11-22) makes a 3D view named "MH Replace -
    elements it would delete" with only those model elements shown, closes Check parameters and opens
    the view, zoomed to them; open Check parameters again to replace. It's greyed out when none of them
    is a model element. The view stays until someone deletes it.
  - It's only offered where the database's parameter can be added, e.g. not while the master file
    lacks it. The values come back from the database when they're applied (step 6).
- **Add N missing parameters** binds them as instance parameters, in one undo step, from Studio's
  master file (decided above). The add-in copies the file to `%LOCALAPPDATA%\MH\RevitTools\`, points
  Revit at the copy for the moment it reads it, then puts the user's own setting back. A file holding
  none of the database's GUIDs is refused, as it belongs to the other database.
- Parameters land in the palette groups the Excel route uses, by group code: KAT and BES under Identity
  Data, GEO under Geometry, ORT under Constraints, SPE under Data, DAR under Graphics, the material
  groups under Materials, the rest under General. Families aren't reloaded and values aren't touched.
- **Walk:**
  1. Start Studio on dev once and sign in, so it writes the dev master file (it isn't there yet).
  2. Open the linked test model → Database → Check parameters. The parameters show as Missing, apart
     from any your model already has (Ready) or has with another GUID (Other GUID).
  3. Click "Add N missing parameters". They turn to Added, and the button says Done.
  4. Select a Generic Model: its Properties show the new parameters with their Revit names, e.g.
     Bauteil_ID under Identity Data and Dicke under Geometry.
  5. Check parameters again: everything that was Missing is Ready.
  6. Replace (added 2026-11-22): on a model with an Other GUID parameter that a schedule shows, click
     Replace… → Replace them → the second warning names the schedule. Replace all: the schedule's
     column goes and the parameter shows Added. Close the dialog: Ctrl+Z brings both back.
  7. Where Revit deletes other elements with a parameter, the list shows them by name before anything
     goes. Delete … and replace: they go, the parameters are replaced, and Ctrl+Z brings it all back.

### 5. The pane and the change check (no apply yet)
- The docked **"MH Database" pane**, laid out like the mockup. The big Database button shows and hides
  it; the first time it docks on the right, then Revit remembers where it was put. Revit only takes
  panes while it starts, so it's registered then.
- **"Changes" means: database values the model doesn't have yet.** The pane compares the model with
  the database, as the mockup's "The model matches the database" says: each element meets its object by
  Bauteil_ID (read like the Excel route reads it, any case), each material its database material by name
  (Revit materials carry the Materialname), and each parameter is found by its GUID. Placeholders
  ([Modeller], [ET], -, TBT, empty) and the Bauteil_ID itself are never changes. Numbers are compared in
  Revit's units, converted from the database's unit as the Excel route converts them, so 0.3 m and 300 mm
  match. One change is one parameter of one object, however many elements carry its Bauteil_ID.
  Bauteilnummer and Bauteilunternummer are values like any other (Yehia, 2026-11-23; since 2026-11-25 on the Excel route
  too); a family's own Bauteilnummer is another parameter, which the link never touches.
- What it shows:
  - The header: a dot and "Connected", "Not reachable", "Project not found", "Not set up" or "Not
    linked"; the database ("CADSQL · MH_Database · read-only"); "Checked 14:32 · every minute" or
    "Last answer 11:05 · tries again every minute"; a Check now button.
  - **This model**: the project, "Change project…" (the Link dialog), who linked it and when, and three
    counts that select their elements when clicked: elements linked, without Bauteil_ID, ID not in the
    project (counted on the categories the database's object parameters go on).
  - **Up to date** ("The model matches the database. Last change there: today 14:31 · S. Brandt."), or
    **N changes from the database**: one card per object (Bauteil_ID, name from Bauteil and Teilelement,
    "12 elements") or material, each value as "model value → database value" with who changed it and
    when, from Studio's history; "Show in model" selects the object's elements and zooms to them. The
    most recently changed come first. Up to 300 values are listed, then "… and N more".
  - What can't go into the model, under "Not applied" (step 6).
  - Not linked: the two steps, "Link the model…" and "Check parameters…". No connection file: where it's
    expected (since step 9: the connection file can't be used, and what's wrong with it). Not reachable: the red banner
    with Revit's reason and Try again; the values stay as last read.
  - The footer: "Look for changes" every 30 seconds, minute, 5 minutes, or only when asked: each person's
    choice, in `%APPDATA%\MH\RevitTools\settings.json`.
- **The check** runs on a thread of its own, so Revit never waits for the database. It checks the active
  model's project at the chosen interval, at once when another model or link becomes active, and on
  Check now. It reads only the change stamp, and everything again when the stamp has moved. The first
  check of a project in each Revit session reads everything, whatever the stamp says: a Studio migration
  can change data without moving the stamp. It reads the connection file at every check, so a file put
  right counts at the next one.
- **A full read also checks the link**: the project with the saved id must still have the model's
  number and sub-number. Otherwise the pane says so and shows no values ("Project not found", or "the
  model's id means 1762-01 Neu-Ulm now"), so another project's values never show as this model's.
- **The last pull** of each project is kept per person on the PC, in `%LOCALAPPDATA%\MH\RevitTools\Pulls`,
  one file per database and project (never the login or password). When the database can't be reached at
  the start of a session, the pane compares with it and says "Values from the last pull: today 09:12".
- **The comparison** runs inside Revit when new values arrive and 1.5 seconds after the model last
  changed (any change, Undo and Redo too). It only reads the model. A parameter is looked for on the
  element, then on its type. A value Revit can't read or that doesn't fit is named under the changes
  instead of stopping the list.
- **The Database button** keeps the project number on its second line; the dot shows the state: green up
  to date, blue with the number of changes (9+ above nine), red not reachable or project not found, grey
  while checking or without a connection file. The dots are drawn at runtime from the plain icon, so the
  green-dot icon files are gone.
- Link project and Check parameters run from the pane's buttons through one external event, as Revit
  requires for a pane. The ribbon's Check parameters stays.
- **Walk** (the dev connection file and Studio on dev):
  1. Open the linked test model, click the big Database button on the Parameter Tools ribbon: the pane
     opens on the right. Type BT-1101-01 into the
     Bauteil_ID of one Generic Model. Within a few seconds the pane lists Bodenplatte's values as changes
     ("(empty) → B500B" and so on), as the model doesn't have them yet, and the button's dot turns blue
     with their number.
  2. In Studio, change one of BT-1101-01's values in project 1761 (e.g. Betonstahl Güte). Within a
     minute (or at once with Check now) its row shows the new value, your name and the time, at the top.
  3. Click "Show in model": the element is selected and zoomed to. Click "1 element linked": it's selected.
  4. Rename the connection file: at the next check the pane says "Not set up"; rename it back and click
     Check now: "Connected" again. To see "Not reachable", put a wrong server name into the file instead. (Since
     step 9, with the PC's own file renamed, the add-in's counts.)
  5. Open an unlinked model, or switch to one: the pane shows the two steps, and the button "Link model".

  Revit stays responsive the whole time.

### 6. Apply and "Not applied"
- **Apply all** over the list, **Apply** on each card (an object or a material), and **Apply changes** on
  the ribbon, which asks first and can show the list instead. Each is one undo step, named in Revit's Undo
  list ("MH database: 17 changes", "MH database: BT-1101-01").
- Apply writes exactly what the pane lists, into the elements it lists, and compares again. A value the
  database has changed since the list was made is left out and listed anew; so is an element that got its
  Bauteil_ID since. While the database isn't reachable the list stays, but Apply
  waits, so nothing outdated goes in.
- It never adds parameters or touches families and types, never writes placeholders, and never writes the
  Bauteil_ID, which the modellers enter by hand. It does fill Bauteilnummer and Bauteilunternummer (Yehia,
  2026-11-23), as the Excel import does since 2026-11-25.
- A banner says what went in ("3 changes applied to 16 elements"), and what didn't and why. Undoing it
  takes the banner back and lists the changes again.
- **Workshared models:** Apply borrows the elements from central first, all in one go. Those someone else
  has, or that changed in central, are left out and named.
- **Not applied** lists, with the reason and what to do:
  - elements someone else has borrowed, or with newer changes in central
  - parameters that are read-only there, or sit on the element's type
  - values that don't fit their parameter: text in a number, a number written with a comma, or a
    measurement whose unit Revit can't convert
  - IDs that several objects (or materials) share in the database with different values: none of them goes
    in, so Apply never goes back and forth between them
  - parameters that aren't in the model ("Check parameters…")
  - materials that share a name: only the first is used
  - placeholders ([Modeller], [ET], -, TBT)
  - until step 11: an object's Material-ID and Materialnummer. In the dictionary they're parameters of materials,
    so elements don't carry them. (The Excel import writes Material-ID to elements by name.) Since step 11 they're left out
    without a word: an object's material is in `revit.ObjectMaterials`.
- **Walk** (the linked test model with BT-1101-01 on one Generic Model):
  1. On the BT-1101-01 card click Apply: its values appear in the element's Properties, a green banner says
     how many, and the button's dot turns green.
  2. Ctrl+Z: the values and the banner go, and the changes are listed again. The Undo list shows
     "MH database: BT-1101-01".
  3. Ribbon, Apply changes: it asks "Apply N changes from the database?". "Show them first" opens the pane;
     "Apply them" applies.
  4. Units: in Studio set BT-1101-01's Dicke from [Modeller] to 0.3. The pane shows "(empty) → 0.3 m". Apply:
     Properties shows 0.30 m (or 300 mm), not 0.30 ft. Set it back to [Modeller] afterwards.
  5. Under "Not applied": the [Modeller] values; and, until step 11, Material-ID and Materialnummer. Type a value into a
     [Modeller] parameter, then Apply: it stays.
  6. Only with a workshared model and a second user: elements the other one has borrowed are listed under
     "Not applied" and stay as they are.
  7. The Excel import, as its writing loop moved: run Add Parameters to Families with Planungsdatenbank.xlsx on
     a test model, as before. Values and counts are the same as they always were.

### 7. Excel and database give the same result (the acceptance)
- A temporary **Compare with Excel** button, in the Database panel's slide-out. It loads a Planungsdatenbank
  workbook with the Excel import's own reader and the model's linked project from the database, and lists
  every difference. It only reads; the workbook may stay open in Excel, and what's loaded for the Excel import
  stays as it is. What's typed into an open workbook and not saved isn't compared: the window says when it was
  saved.
  - **Objects** and **materials** by their IDs; a material's name, group and fill patterns too.
  - **Values** after unit conversion: what Revit would get from each, with the Excel import's conversion. Only
    rounding is forgiven: whole numbers must be the same, others up to a billionth of their size.
  - **Parameters** by set, group and name (BAU-GEO-Dicke), as the two number them differently, and whether Revit
    takes them the same. One filed under another set on one side is still the same parameter, said once.
  - What's known to differ is listed apart and doesn't count: the columns the Excel reader leaves out
    (Bauteil_ID, a material's Material-ID; until 2026-11-25 also Bauteilnummer and Bauteilunternummer), the fill pattern columns, global
    materials, and parameters only the database's dictionary has.
  - What one side gives twice, differently, counts as a difference: only the first is compared.
  - What the Excel reader leaves out or questions (a repeated Bauteil_ID, a row without one, a material without a
    name) is listed as its remarks. With any, the headline never says "Identical".
  - "Copy the list" puts all of it on the clipboard.
- **What it finds today** (the workbook of 2026-11-12 against 1761-01 as kept on 2026-11-23, 14:49; run outside
  Revit, with a stand-in for Revit's unit conversion):
  - **The same:** all 109 material values the database has, the name, group and fill patterns of all 38 common
    materials, what Revit makes of every common parameter, and BT-1101-01's values but two.
  - **Gaps:** 14 material parameters (472 values) and two object parameters that the Excel import writes and the
    database doesn't give as values, and 23 global materials. Yehia leaves them for now ("Needs from the
    database", item 5).
  - **Filed under another set** (Excel: IDE; database: BAU or REV): Betonstahl Mat.-ID, Darstellung-ID,
    Oberfläche-Ansicht_ID, Oberfläche-Schnitt_ID. The link goes by GUID, so it doesn't matter to it.
  - **Test data:** three objects only in the Excel (BG-3454-02, EB-6565-01, ET-3333-01), one only in the database
    (ET-2101-01), and BT-1101-01's Bauwerk ("nope") and Bauteilkategorie ("Bauteil?") changed in Studio.
  - **Not tested by this data:** units. The only common object has no measurement in both.
- **Walk:**
  1. On the linked model, Database panel → the arrow under it → Compare with Excel → pick
     Planungsdatenbank.xlsx. The list comes up; Copy the list.
  2. For the units: in a copy of the workbook give BT-1101-01 a Dicke of 0.3, save it, and give it 0.3 in Studio
     too. Compare with the copy: Dicke must not show up.
  3. Change Studio's to 0.31 and compare again: Dicke must show up, with both values.

### 8. Sign in with your Studio account
- Revit asks once per start, the first time the database is needed. Nothing is saved: who signed in is known until
  Revit closes, the password only while it's typed. A wrong password or an inactive account means no link.
- **What needs it:** everything that reads the database: the pane's check, Link, Check parameters, Apply changes and
  Compare with Excel. Test connection doesn't, as it reads no project's data.
- **Where it's asked,** as in the mockup:
  - The pane shows "Not signed in" with a grey dot, a card with **Sign in…**, when the last pull was, and the model's
    counts by that pull. Nothing is checked, no change is listed and nothing can be applied.
  - The Database panel's third row says **Sign in…**, then the person's name. Clicking the name offers to sign out,
    or to sign in as someone else.
  - A command that needs it asks first, then goes on.
- **The window** is the mockup's. One click on Sign in is one try, in the background, and it's never repeated by
  itself. A refusal says the same for a wrong password, an unknown name and an inactive account. When the database
  makes a name wait, the button counts it down; another name can try meanwhile.
- The name is trimmed and the password isn't, as in Studio's sign-in. The hash is Studio's (`PasswordHash`), pinned
  to the same test value as Studio's own. The sign-in's connection is always encrypted, whatever the connection file
  says (except LocalDB, where nothing leaves the PC): the hash is as good as the password to whoever reads it.
- A sign-in holds for the database of the connection file. Another one asks again.
- With someone signed in, the pane's header says "Signed in as … · until Revit closes".
- **Walk:**
  1. Restart Revit and open the linked model: the pane says "Not signed in", the Database button's dot is grey, and
     the panel's third row says "Sign in…".
  2. Check parameters: the sign-in comes first. Cancel: nothing opens.
  3. Sign in… with a made-up name: refused. With your own account and a wrong password, once: the same words.
  4. With the right password: the pane checks, the row shows your name, the header says who is signed in. Check
     parameters now opens straight away.
  5. Click your name → Sign out: back to "Not signed in".
  6. The waits, with a made-up name so your own account isn't blocked: five tries in a row, and the button counts
     down from 30 s.
  7. Restart Revit: it asks again.

### 9. Publish it for colleagues (installer)
- Yehia, 2026-11-23: step 9 comes before 10 and 11, and its scripts follow the way Release Manager does installation
  and the share (`C:\Yehia\ReleaseManager`, which runs each project's own scripts). Yehia, 2026-11-24: for now without the
  lock and the hand-over.
- **Two halves.** The database session makes the read-only login and its password ("Needs from the database", item 4):
  prepared, see "What the database session built". The connection file, and asking for the password, is this session's.
- **The connection file is built into the add-in** ("The connection file"). `New-Package.ps1`, `Publish-ToShare.ps1`, `Install.ps1`,
  Check for Updates and Release Manager bring it along without knowing, and none of them is changed. Colleagues only install
  or update the add-in, without administrator rights. Neither the package nor the share has a separate file.
- **Revit asks for the database password** where the sign-in is, before the Studio account: the first time, and again when the
  server refuses the one that was kept. Test connection asks too. The window shows which server and database it's for. One
  click on Continue is one try, in the background; spaces and line breaks around a pasted password don't count. Only one that
  works is kept ("The connection file", The password).
  - **A refused password is never tried again by itself:** that doesn't make it right, and every PC would fill the server's log
    with it. (It can't lock the login: the office's has no lockout.) The pane's check then says "Not signed in" and why, and signs
    the person out; Sign in… asks for the new password, then for the account. After a restart the old one is tried once more,
    with the sign-in, as a refusal may have been the server's fault.
  - A server that can't be reached, a timeout, a missing database, a disabled or locked login or a certificate problem is never
    taken for a wrong password. With the login disabled, the pane says what the server said and tries again at its next check.
  - For a new password (or when a colleague leaves): the database session's script makes one, and the old one stops at once. An
    open Revit notices at its next check. Yehia sends the new one, and Revit asks for it.
- **Publishing,** from wherever Yehia is:
  1. The database session's half, by its checklist (`MH.DatabaseStudio\RevitLogin\CHECKLIST.md` in ProjectsApp), with the VPN:
     Studio's release with the views and the sign-in, the login, the check. Yehia runs all of it.
  2. **Only once Yehia says that `Test-RevitLogin.ps1` said "All right":** Release Manager, Revit Tools 2027 (MultiVersion), in Live
     mode: Check readiness, Update version, Build add-in, Install for testing, I tested this build, Create installation files
     (the ZIP), Preview publication, Publish add-in, Verify shared files. It builds the folder that its Locations name (Revit
     repository root). 5.3.0.0, without the link, came from the release folder (2026-11-25, below); the link needs one that has it,
     like this one. (Release and Promote are Database Studio's.) The version has to be higher than the share's (5.3.0.0).
  3. Yehia sends each colleague the password in a private Teams message.
  4. Colleagues: Check for Updates, or `Install.cmd` from the ZIP, run as themselves and not as another administrator (Revit
     2027 reads the manifest from the person's own `%APPDATA%`). Then Sign in…: the database password once, then their Studio
     account.
     - Check for Updates: close every Revit 2027 window. **The version that's installed does the updating,** so whoever comes from
       5.2.2.0 still has the old script, which says "Update Complete." whatever happens (2026-11-24, below); "Sharing violation" in
       its window means that it didn't go through. After the restart, Check for Updates has to say "You're already using version …"
       with the new number; otherwise once more. From the new version on, the corrected one does it.
- **Walk:**
  1. On your PC, now, without the office, VPN off:
     1. With your own file, nothing has changed: Sign in… asks for the account only, the pane checks, and Test connection's
        details name `C:\ProgramData\MH\RevitTools\database.json`.
     2. Rename that file to `database.dev.json`, then Check now in the pane: "Not signed in · CADSQL · MH_Database", you're signed
        out, and the card says that Revit also asks for the database password.
     3. Sign in… opens the password window, for CADSQL. Continue without anything: it asks for the password. With any made-up
        text: it can't be checked, as CADSQL can't be reached, and isn't kept (there's no
        `%LOCALAPPDATA%\MH\RevitTools\Passwords`). Cancel: nothing else opens.
     4. Test connection, Link, Check parameters and Compare with Excel ask for it first, too; Cancel: nothing opens.
     5. Rename the file back, then Sign in…: your account only, and the pane shows dev again.
     6. Take a comma out of the file, then Check now: "Not set up", with the line of the mistake, and nothing about CADSQL. Put it
        back, Check now: as before.
  2. Also without the office, if you like: **the whole way of the password, on dev.** It needs the login on your LocalDB, which is
     yours and the database session's to make (`New-RevitLogin.ps1` takes `-Server '(localdb)\MSSQLLocalDB' -WindowsSignIn` and
     a `-PasswordFile` of its own, so that the office's isn't mixed up with it). Then, in your own file, `"login": "revit_reader"`
     instead of `"windowsSignIn": true`:
     1. A wrong password is refused and isn't kept; the right one is, and the account follows. Restart: only the account.
     2. `-NewPassword` while Revit is open: its next check says that the server refused the password, and signs you out. Sign in…
        takes the new one. The old one is refused.
     3. The login disabled: the pane says so and doesn't ask for a password. Enabled again: the next check works.
     4. Your file as before.
  3. On your PC, with VPN, once the login exists: 1.2 and 1.3 with the right password: the sign-in follows, then Link lists
     the office's projects. Use a test model that isn't set up on dev: the office's parameters have GUIDs of their own. Restart
     Revit: it asks for the account only. Test connection's details say "built into the add-in". Rename the file back.
  4. Publish. One colleague in the office updates with Check for Updates: Sign in… asks for the password once, and Test
     connection's details say "built into the add-in".
  5. A colleague installs from the ZIP with `Install.cmd`: the same.

**Not used: the lock** (`Revit.Addin.2027\Scripts\ConnectionFile`, with a README of its own; built 2026-11-24). It was for a
connection file that holds the password. As the PC's own file wins, it could still come without a change to the add-in.
- **Separate from `Install.ps1`,** unlike first planned (2026-11-23):
  - A PC that already has the add-in needs only the file, once. Check for Updates brings later versions and never touches it.
  - It needs administrator rights. Revit 2027 reads the add-in's manifest from the person's own `%APPDATA%`, so the add-in's
    installer has to run as that person, not as another administrator.
  - The add-in's package and ZIP never hold the password.
  - `Install.ps1`, `New-Package.ps1` and the others stay identical in 2024, 2026, 2027 and ProjectsApp.
- **`New-ConnectionFilePackage.ps1`** (developer side) makes the folder to hand over,
  `C:\Yehia\ProjectsApps\Revit.Addin\ConnectionFile`, from `C:\Yehia\ProjectsApps\Revit.Addin\database.json`. Only Yehia and
  administrators can open it. Never in a repository, OneDrive, a network folder or the share, and never a ZIP.
- **`Install-ConnectionFile.ps1`** (on each PC, as administrator, by double-click on `Install connection file.cmd`) writes
  `C:\ProgramData\MH\RevitTools\database.json` and locks `MH` and `MH\RevitTools`: administrators and SYSTEM full control,
  users read only, nothing inherited, owned by the administrators. By default any user may create files under
  `C:\ProgramData`. Without the lock, anyone could put a file there before the installer runs, or own the folder, and point
  Revit at another server. So an `MH` that's there is never taken over: a new one is built beside it, locked from the start
  and with the file in it, and takes its place. An unlocked one is kept as `MH.before-<time>`.
  - It stops, and changes nothing, when the file is a template, isn't complete or is a developer's; when the PC's file is for
    another database (`-Force` replaces it); when a folder is a link; or when an open `MH` holds something else.
  - It never shows the password.
- **The add-in's folder isn't locked,** unlike first planned: Check for Updates writes there without administrator rights.
- As in Release Manager: named parameters and no questions, a preview that writes nothing (`-WhatIfOnly`), a check read back
  from Windows, an exit code. Release Manager itself isn't changed.
- **Walk, when it's used:**
  1. On your PC: `New-ConnectionFilePackage.ps1 -WhatIfOnly`, then without it. The folder is there.
  2. On a second PC with the add-in: "Install connection file.cmd", Run as administrator. Then Test connection (in the Database
     panel's slide-out) works, and the pane asks to sign in.
  3. There, as a normal user, try to change or delete the file: Windows refuses.

### 10. Create the project's missing materials
Yehia, 2026-11-24: "we proceed to step 10", while step 9's walk waits for the office. Before this step the link only filled
parameters of materials that are already in the model. The Excel import's Automate Material Creation also creates them and
sets their fill patterns. This step does the same from the database. It needs nothing from the database session.
- **The pane** has a section "Not in this model · 38 materials". Its title shows or hides the names. **Create…** asks first, and
  creates them all in one undo step, with their values. Apply all doesn't create materials, and missing ones are no changes:
  the pane can be up to date ("What the model has matches the database").
- **Which:** the project's own materials (`revit.Materials`) whose Materialname the model doesn't have, as the Excel
  import does. Two with the same name: the first, as everywhere. Global materials aren't in the views.
- **What they get:** the Materialname, the material class by the group (Beton → Concrete, …), a look copied from a
  material of the same kind in the model and structural properties, with the Excel import's own code
  (`MaterialCreation.Create`, taken out of its loop; the import works as before); then the fill patterns, and the parameter
  values by the same loop and rules as Apply.
  - A group that code doesn't know gets the class Generic and a default look, as with the Excel import. In dev's 1761 that's
    "Boden (in situ)", 9 materials, and Bitumen, 1.
  - Structural properties of that name that are in the model already, left over from a deleted material say, are taken (the
    link only): Revit refuses to make them a second time.
- **Fill patterns,** also of materials that are there: "Surface pattern" and "Cut pattern" are rows on the
  material's card, and Apply sets them. An empty one in the database, or a placeholder, leaves the material's as it is. The views
  don't say who changed a pattern, or when.
  - Only patterns the model has (and Solid). Others are listed under "Not applied". The Excel import copies missing
    ones from a model file whose path is fixed to Yehia's PC (`Services\FillPattern.cs`), so on other PCs it can't.
    A file for the office could come later, named in the connection file like the master parameter file.
  - A cut takes drafting patterns only (Revit's rule), so a model pattern of that name doesn't count there.
- **Not created:** a material without a Materialname or a Materialgruppe (listed under "Not applied"), and one Revit refuses, for
  a character in its name say (the pane says it after Create).
- A material renamed in Studio is another material to Revit, as materials meet by name: it's offered as missing.
- **Walk** (dev; the office isn't needed):
  1. Open a linked model of 1761 that lacks some of its materials: the pane says how many aren't in the model, and the
     section's title shows their names.
  2. Create…, then Cancel: nothing happens. Create…, Create them: they're in Revit's Materials with class, look and values, and
     with their patterns where the model has them. The pane says how many it created; the section is gone.
  3. Ctrl+Z takes them all back, and the section is there again.
  4. Create them again, delete one in Revit's Materials: the pane offers it, and Create… makes it.
  5. In Studio, set a material's pattern to one the model has (Solid, say): it's listed, and Apply sets it.
  6. A pattern the model doesn't have is listed under "Not applied".
  7. The Excel import's Automate Material Creation works as before.

### 11. Assign materials to objects
- **It needs a view:** `revit.ObjectMaterials` ("Needs from the database", item 6), in dev since 2026-11-24. Since 2026-11-11 Studio
  keeps an object's material in a field of its own (`ProjectObjects.MaterialId`). The Material-ID and Materialnummer among an
  object's values are copies of it: until 2026-11-25 Studio's Projects import still stored Material-ID, since then nothing stores
  either, and dev has none left. The link leaves out any that's still there, without listing it.
- Not to be mixed up: Revit has two parameters called Material-ID. 00031 (IDE-KAT) is the one above, of materials. 00019
  (PRI-MAT), the material of the object's product, is a value of the element like any other, and stays one.
- What there is to walk with, in dev's 1761: BT-1101-01 is made of 13-Beton, C20/25; ET-2101-01 and BG-1001-89 of nothing yet.
- **The pane** lists an object's material like any other change: "Material: <By Category> → 13-Beton, C20/25", with who and
  when. Apply assigns it, in the same undo step as the object's other values, to the elements it's listed for.
- **Where it goes:** the element's own parameter "Material" that takes a material, as in the Excel import. The material is
  found by its Materialname, whatever its case. The link never creates one for that, never adds the parameter, and
  leaves a type's alone.
- **Not applied,** with the reason:
  - the material isn't in the model. Create… makes it if it's one of the project's own; it doesn't make a global one, another
    project's, or one without a group. Once the model has a material of that name, it can be applied.
  - the elements have no parameter for it, it sits on their type, it's read-only there, someone else has borrowed them, or they
    have newer changes in central
  - two objects share the Bauteil-ID and are made of different materials; alike, they're one
  - the name stands for another material: Revit has one per name, so it's the first of the project's own of that name
- An object without a material in Studio leaves the element's as it is, also when it was cleared there.
- An object with nothing but a material counts as one of the project's ("elements linked").
- Global materials are assigned when the model has one of that name; they aren't created.
- **Compare with Excel** also compares what each object is made of, by the Materialname: "Objects made of another material". The
  workbook's Material-ID and Materialnummer among an object's values are known then ("By design").
- Materials' values for parameters that go on elements (none on dev) are named as such under "Not applied"; they used to be
  taken for references.
- The kept pulls have a new layout, so the ones from before aren't read: a PC that can't reach the database shows no values
  until it has reached it once.
- **In code:** `LinkObjectMaterial`, `ObjectMaterial`, and `CompareMaterial` for `ChangeFinder` in the library; `RevitMaterials` in the add-in,
  which the pane's list and Apply both ask.
- **Walk** (dev, the linked test model of 1761). Yehia, 2026-11-25: "revit automatically adds the Material parameter to any element so we
  dont need to add one specifically". The link takes whichever "Material" the element has, Revit's own or the family's; not checked
  from here that every element has one, so point 6 shows it.
  1. The card BT-1101-01 lists "Material … → 13-Beton, C20/25", with your name and when you set it. Apply: the element's Material shows it.
     Ctrl+Z takes it back, and it's listed again.
  2. In Studio, on the object's Material tab, give BT-1101-01 another of the project's materials that the model has: within a
     minute it's listed. Apply. Give ET-2101-01 its first one: the same.
  3. Give BT-1101-01 one the model doesn't have (or delete it in Revit's Materials): under "Not applied", "The material … is not in this
     model", and that Create… makes it. Create…, then Apply.
  4. Give it 0101-Beton, a global one: under "Not applied", it says that Create… doesn't make it. Make a material of that name by hand:
     it's listed, and Apply assigns it.
  5. Clear its material in Studio: nothing is listed, and the element keeps what it has.
  6. An element whose family has no Material parameter, or only the type's: under "Not applied".
  7. Material-ID and Materialnummer aren't under "Not applied" any more.
  8. Compare with Excel: "Objects made of another material" has BT-1101-01, as the sheet says 31-Beton, C35/45.
  9. With a workshared model and a second user: what the other one has borrowed isn't touched.

### 12. Fill the material's own fields
Planned on 2026-11-25, nothing built. Yehia: "only the parameters that live under "Values of group (BET for exmaple)" in Studio that gets
its values added, Materil group, category, Material standard materila number materila numbner2 and name 2 do not"; and, asked whether the
link should fill these too: "Yes it should, so plan it".
- **What:** twelve parameters of materials: Material-ID, Materialkategorie-ID, Materialkategorie, Materialgruppen-ID, Materialgruppe,
  Materialnummer, Materialnummer2, Materialname, Materialname2, Bezeichnung, Bemerkung, Materialnorm. All are in the dictionary with Target
  Material, so Check parameters has them already.
- **It needs** more rows of `revit.MaterialValues` ("Needs from the database", item 7). No new view and no new column: the add-in as it is
  lists and applies them as soon as they come. An add-in with step 12 works without them too; it just doesn't fill these fields.
  - So the rows go into dev only after step 11's walk. Otherwise the pane lists about 400 more changes during it.
- **As with the material's other values:** the pane lists them on the material's card, with who last changed the material and when; Apply
  writes them; Create… fills them in what it creates. A field that's blank in Studio leaves the model's as it is, a placeholder is under
  "Not applied", and so is a parameter the model doesn't have, until Check parameters has added it.
- The Excel import fills all of them but the Material-ID, which it only uses to tell the rows apart.
- **What's to build here:**
  1. The tests' copy of the view, the stand-in columns, and LocalDB tests: every field, a blank one, one row per material and parameter, who
     and when, the stamp.
  2. Line breaks: Studio's Note can have several lines. Texts that differ only in the kind of line break are the same, in the pane's
     list and Apply (`RevitValues.IsSame`) and in Compare with Excel (`LoadedValue.IsSameAs`): one rule, in the library. Whether Revit
     keeps the lines in a text parameter, the walk shows.
  3. Nothing else: the kept pulls keep their layout, and Compare with Excel knows the Material-ID as "By design" already.
- **How much:** about 400 rows for dev's 1761 if its 38 materials say what the workbook says (Bezeichnung 15, Bemerkung 16, Materialnorm 23). The
  pane shows the first 300 changes; Apply all applies all.
- **Not part of it:**
  - Projekt-ID, Projektnummer and Projektunternummer, which the workbook also has on its material sheet: in the dictionary they're
    parameters of Generic Models, so a material doesn't have them.
  - Global materials, as before.
  - The material's class in Revit, which is set when it's created: another group doesn't change it.
  - A material renamed in Studio stays another one to Revit (step 10). With the Material-ID in the model, the link could tell that it's
    the same one; not planned.
- **Walk** (dev, the linked test model of 1761), once the rows are in dev:
  1. Check parameters: all twelve are there, or it adds the missing ones.
  2. The materials' cards list the fields. Apply all. In Revit's Materials, 11-Beton, C12/15 has them, and nothing is listed again. Ctrl+Z
     takes them back.
  3. Delete a material in Revit's Materials; Create… makes it with these fields.
  4. In Studio, change a material's Material standard: within a minute it's listed, with your name. Apply. Clear it: nothing is listed, and the
     model keeps its value.
  5. A Note of two lines: Apply, and it isn't listed again.
  6. Rename a material group in Studio: its materials list the new Materialgruppe. Put it back.
  7. Compare with Excel: of "Material values only in the Excel", only Projekt-ID, Projektnummer and Projektunternummer are left (114 of 472), as
     far as Studio's materials say what the sheet says.

### After step 11: the final product as the latest commit
Yehia, 2026-11-25: "Onnce we finalize step 11, we wanna have a commit of the database-link merged with the release-5.3 so that we have
the final product as the latest commit".
- Once step 11 is walked and committed on `database-link`: **one merge commit of `database-link` and `release-5.3.0`,** pushed. It's the
  newest commit then and holds everything: the released 5.3.0.0 with the new updater in all three years, and the link in 2027.
- It's on master (Yehia, 2026-11-25: "master"), which is `release-5.3.0` now. `release-5.3.0` stays at e15e78a: what colleagues have, for
  a fix without the link.
- Before it: all tests, also the ones that need LocalDB (Yehia runs those), and all three years compiled. After it: the result
  compared with what was walked. Tried on 2026-11-25 without changing anything: no conflicts, and the result is `database-link`'s own
  files, as it has the released changes already.
- It isn't a release. That stays Yehia's, with a version above 5.3.0.0, and only once `Test-RevitLogin.ps1` said "All right" and step 9
  is walked.
- Step 12 came up the same day. Whether the merge waits for it is Yehia's to say.

**Later (phase 3, not now):**
- auto-apply when Revit is idle
- filling new elements as soon as their Bauteil_ID is typed (`Updaters\ElementUpdater.cs` still keys by
  Bauteilnummer1 and shows debug dialogs)
- German UI

## Where Revit differs from the mockup

The updated mockup with these changes (19 November) is at
https://claude.ai/artifact/TVSdpLgGotaWpp4PNro3Ed. It is now the reference for the add-in. The first
mockup (https://claude.ai/artifact/BUxmo897HJTFcEt43wpcen) stays as it was.

- **Connect dialog:** it goes away, because the server, database and read-only login come from the
  connection file. A small sign-in with the Studio account replaces it, together with the per-user
  settings (how often to check, auto-apply).
- **Check parameters, "Replace…" on Other GUID:** left out at first, as replacing deletes the old
  parameter's values. Back at Yehia's request (2026-11-22): it asks first and is one undo step, and it
  asks once more for parameters that schedules or view filters use (step 4).
- **Compare with Excel** isn't in the mockup: it's temporary (step 7) and sits in the slide-out.
- **The database password** (step 9) isn't in the mockup: a window of its own before the sign-in, once per person and PC.
- **The Database panel** follows the mockup since 2026-11-22: the big Database button, then three rows
  (Apply changes, Check parameters, Sign in). Database shows and hides the pane (since step 5; before,
  it opened the Link dialog). Test connection sits in the panel's slide-out (the arrow under the panel). The
  small icons are a lighter grey than the mockup's, so they also show on Revit's dark ribbon.
- **Sign in** (step 8): clicking the name in the ribbon says who is signed in and offers to sign out or to sign in
  as someone else; the mockup only opens the sign-in there. The button counts a wait down, which the mockup doesn't
  know. An unlinked model keeps the two setup steps, and the sign-in comes with the first of them.
- **The Database button's second line** is the pane's state in the mockup ("3 changes", "Up to date",
  "Link model"). Here a linked model keeps its project number there, and the dot on the icon shows the
  state instead: green up to date, blue with the number of changes, red not reachable, grey while
  checking (this session's proposal of 2026-11-22; Yehia on 2026-11-23, after the walk: "the buttons
  meanings are good and are reflected"). The dot sits inside the 32 px icon, as Revit's icons can't reach past their edge.
- **The pane** leaves out the "Apply changes automatically" switch (later, phase 3). "Apply" on a card applies
  the whole card, as in the mockup. When values differ but none can go in now, the green card says "Nothing to apply now" instead
  of "Up to date". The ribbon's Apply changes asks before it applies, as the list may not be in sight. An
  unlinked model's header says "Not linked" rather than "Connected", as nothing
  is checked for it. The model section's line says who linked the model and when. Material changes are
  listed too, by Material-ID and name. A value shows as Revit shows it on the left and as the database
  holds it, with its unit, on the right ("300 mm → 0.3 m" would be a real change; equal values aren't
  listed).
- **Who and when on each change** ("M. Keller · 14:28"): from Studio's change history, as the history
  names the person.
- **Docking:** the pane opens on the right the first time. After that Revit remembers where the user put
  it. Revit only allows a pane to be registered at startup, so the Database button shows and hides it.
- **Master shared parameter file:** Revit only opens the shared parameter file set in its options. The
  add-in switches Revit to a local file for a moment, then puts the user's own setting back. Never to
  Studio's file itself: holding that open would block Studio's next write.
- **Fonts:** Segoe UI, which is what Revit uses, instead of Inter.

## What the database session built (2026-11-19)

Its side of the contract, with every detail, is `REVIT_LINK.md` in
`C:\Yehia\Database_parameters\VisualStudio\Database_Creator\ProjectsApp\Database.Data\`. In short:

- **The views** are in dev MH_Database, from the Studio migration `20261119183135_AddRevitLink`. The
  office gets them with the next Studio release.
  - They are `RevitViews.sql` with two lines changed: `revit.Parameters` now reads each parameter's GUID
    and Revit name from `dbo.Parameters` (`SharedParameterGuid`, and `RevitName`, which falls back to
    the plain name until Studio has set one).
  - The migration is now the one source of the views. The tests' copy here follows it (brought in line
    with step 3).
  - Checked here on 2026-11-19: the seven views have exactly the columns the add-in reads, and all 117
    parameters have their own GUID.
- **The master shared parameter file.** Studio writes it a few seconds after any dictionary change, and
  once after each sign-in. There's one file per database:
  - dev MH_Database (LocalDB): `%LOCALAPPDATA%\MH Database Studio\Development share\PLDB\01.Datenbank\MH_Parameter.txt`
    on the PC where Studio runs. It appears the next time Studio starts on dev.
  - the office database: `\\CADSQL.mh-frlg.local\PLDB\01.Datenbank\MH_Parameter.txt`. Not written yet;
    that comes with the release.
  - A path typed into Studio's settings (Administration → Settings → Revit) wins over both.
  - **Keep the pairs together.** Each database makes its own GUIDs, the office's at the release's first
    start. A model set up from the dev file won't match the office database, so a test model set up on
    dev stays a dev model.
- **The change stamp.** Studio saves the history row right after the change, so the stamp never moves
  before the change can be read. A migration changes data without history rows, though, and then the
  stamp stays put; hence the full read at the start of each Revit session (step 5). If a history row
  fails to save, Studio tells the user, and the stamp waits for the next change.
- **Material categories and groups** are in the history like everything else, so renaming a code moves
  the stamp. The history keys keep their shape, and a Studio test fails if they change.
- **Values:** Boolean is stored as `true`/`false`, Date as `yyyy-MM-dd`, numbers invariant with a dot.
  Placeholders come through as stored, so the add-in keeps skipping all four.

**The sign-in procedures (2026-11-23),** from the Studio migration `20261123154250_AddRevitSignIn`, in dev
MH_Database. Details: REVIT_LINK.md, "Signing in to Revit". They are what was asked for below, and:
- **Waits:** the 5th wrong try 30 s, the 6th 1 min, the 7th 2 min, from the 8th 5 min. A right sign-in or a day
  without a wrong try clears them.
- **Errors:** inside a transaction (`INSERT … EXEC` too) `CheckSignIn` refuses with 51000, so the add-in's
  connections say `Enlist=false`. 51001: the server's secret is missing. A call that timed out may have been
  counted, so it isn't repeated by itself.
- **Names:** case and trailing spaces don't matter; everything else does (accents, ß and ss). Over 100 characters is
  an unknown name. The procedures compare names themselves, not by collation; Yehia agreed.
  `Users.Username` on dev: SQL_Latin1_General_CP1_CI_AS. CADSQL isn't confirmed; its database was restored from
  dev's backup, so it should be the same.
- **Wrong tries on dev count too.** The database session or Yehia clears them with
  `DELETE FROM dbo.RevitSignInWaits;`. The add-in session calls `CheckSignIn` only on throwaway databases, as a try
  is written down.

**The read-only login (2026-11-24),** prepared, tested and pushed (ProjectsApp, master 7c38707, its step 14). It isn't made yet, and
nothing has gone to CADSQL. Details: REVIT_LINK.md, "The read-only login". The scripts and the day's order:
`MH.DatabaseStudio\RevitLogin`, `CHECKLIST.md`.
- **The connection file's six values are right** ("The connection file").
- **Login and user `revit_reader`, role `revit_readers`:** SELECT and EXECUTE on schema `revit`, nothing else. Default database
  MH_Database, default schema `revit`, no rights on the server. The password is 40 letters and digits and doesn't expire.
- **No lockout** (CHECK_POLICY OFF): it's shared, so wrong tries by anyone would lock it for everybody. So a refused
  password is always 18456 and a disabled login 18470; locked (18486) can't happen.
- **A new password stops the old one at once,** for pooled connections too. Tried on LocalDB with SqlClient 5.2.2: the next
  use throws 18456 at ExecuteReader (errors 18456, 596, 0), and from then on at Open. A query that's running finishes.
  Disabling the login does the same with 18470.
- **The add-in fits both** (checked here on 2026-11-24, reading the code and with tests of SqlClient's own exceptions; nothing
  had to change). Its three places that ask the server have Open and ExecuteReader in one try. 18456 as the first error
  makes it forget the password; 18470 doesn't. Only the first error counts: a database that can't be opened also answers
  18456, after its 4060.
- **The views and the procedures are unchanged;** `RevitViews.sql` and `RevitSignIn.sql` here match the migrations, batch by batch
  (compared by the database session on 2026-11-24).
- **TheAdmin's first-start password** isn't in use on dev. The office's Yehia checks, with the VPN.
- **Waits for CADSQL:** its version, SQL logins and who may make one, the collation of `Users.Username`, the certificate, Studio's
  release with both migrations and the master file, the login, the check. Yehia runs all of it and brings the answers.
  The add-in's package is published only after Yehia says that `Test-RevitLogin.ps1` said "All right".

**The material of each object (2026-11-24),** `revit.ObjectMaterials`, from the Studio migration
`20261124204245_AddRevitObjectMaterials` (ProjectsApp, its step 15). It's in dev MH_Database, and committed there once step 11 is built
and both are tested together. Details: REVIT_LINK.md, "The material of each object".
- **It's what item 6 asks for,** columns and rules. The seven other views, the procedures and the grants are unchanged.
- **The office gets it with the same Studio release** as the other two migrations; `CHECKLIST.md`, `Get-ServerFacts.ps1` and the login's
  check have it. An add-in that reads it needs that release: without the view the read fails, and the pane says that the
  revit views don't match.
- MaterialId is NULL for a material without a full ID; who and when are NULL without a history row. After a review, ChangedBy is
  the reviewer who approved it.
- **The stamp moves** when a material is set, changed or cleared: on the Material tab, by the import, by an approved proposal. Not when
  the same one is set again, nor while a proposal waits.
- Renaming a material shows the new name at once. Who and when stay, and the stamp moves.
- Two objects with one Bauteil-ID are two rows, which can be alike in every column. Dev has no such pair.
- Studio doesn't make Materialnames unique; dev's 61 all differ.
- A row can have another project's MaterialId: Studio lets a material in use be moved to another project, and the object keeps it.
  It's then not in `revit.Materials` of the object's own project.
- **Material-ID (00031) and Materialnummer (00038) on objects:** since 2026-11-25 the Material tab is the only place that holds an
  object's material. The Projects import sets it from Material-ID, or else from Materialnummer, and stores neither; until then it
  still stored Material-ID. Studio no longer offers the parameters with Target Material on an object. Dev has no such value left,
  but one that's there stays until someone removes it.
- **Two parameters are called Material-ID,** in Revit too: 00031 (IDE-KAT, Target Material) and 00019 (PRI-MAT, Target Object), the
  material of the object's product. 00019 stays and goes to the element like any other value. BT-1101-01 has the database session's
  test value 89z98 there, unless it's removed.
- The Betonstahl (00114 to 00116) stays three values with Target Object. That and the PRI parameters come after the link.
- **Dev's 1761 on 2026-11-25:** one object has a material, BT-1101-01 (Bodenplatte): 13-Beton, C20/25 (MB-1761-01-BET-0013), set by
  Yehia Abdelazziz at 01:23 UTC. ET-2101-01 (Schlitzrinne links) and BG-1001-89 (Abdichtungsträger) have none yet. Ankergruppe,
  Ausbruch and Schlitzrinne have no Bauteil-ID, so the view leaves them out. A global one to try: 0101-Beton (MP-9999-99-BET-0101).
- The Planungsdatenbank says MB-1761-01-BET-0031 for BT-1101-01, so Compare with Excel differs there. The sheet wins when it's
  imported.
- **Dev's LocalDB can get stuck after Revit has closed:** a server process that Revit started blocked every new start on
  2026-11-25. The database session clears it; this session never stops LocalDB.

## Needs from the database

*Final version, 2026-11-19. Items 1, 2 and 3 are built and the questions are answered (see "What the
database session built"). Item 4 is prepared and waits for CADSQL (2026-11-24), item 5 isn't wanted for now, item 6 is built
and in dev (2026-11-24), and item 7 is asked for (2026-11-25).*

**What the link needs, in order**
1. **Now: the `revit` views.** The SQL is ready in the add-in repo (branch `database-link`):
   `MH.RevitTools.Database.Tests/Link/RevitViews.sql`. Run it in MH_Database, for example from a Studio
   migration.
   - GO isn't T-SQL: give each batch between the GO lines its own `migrationBuilder.Sql` call.
   - It's safe to run again: the schema is created only if missing, and `CREATE OR ALTER` keeps grants.
   - The migration's Down drops the seven views, then the schema.
   - The add-in's tests run this same script on stand-in tables and on dev MH_Database. One test also
     runs it as a user who has only the `revit` grant, so it works on today's tables and with that
     grant.
2. **Now, for step 4 (Check parameters):** a GUID and a Revit name for each parameter in the dictionary.
   - Then replace the two placeholder lines in `revit.Parameters` with the real columns.
   - The Revit name is the plain name, with a suffix where plain names clash on one category.
   - Studio writes the master shared parameter file from them, in one go (to a temporary file, then
     rename), so the add-in never reads half a file. Tell me its path on the share.
3. **For step 8:** the two sign-in procedures (below). Built on 2026-11-23.
4. **For step 9:** the read-only login and its password (below). Prepared on 2026-11-24, see "What the database session built".
   The login itself waits for CADSQL, which can't be reached for now. The connection file is this session's, as it ships
   without the password.
5. **Not for now (Yehia, 2026-11-23). From step 7, in case the link is to fill what the Excel import fills:**
   values that the views don't give, so the link can't write them. Each has its parameter in the dictionary
   already.
   - Materials: their own fields are wanted since 2026-11-25, see item 7. Still not wanted: Projekt-ID, Projektnummer and Projektunternummer,
     which the workbook has on its material sheet too, but which the dictionary has as parameters of Generic Models.
   - Objects, as rows of `revit.ObjectValues`: Bauteilkategorie_ID and Bauteilgruppen-ID.
   - Global materials (MP-9999-99-…): 23 in the workbook, none in `revit.Materials`, as they belong to no project.
     Should a project's model get them too?
   - Their history (who, when) and the change stamp should cover them like the other values.
6. **For step 11 (asked for and built on 2026-11-24): `revit.ObjectMaterials`,** the material each object is made of
   (`ProjectObjects.MaterialId`), one row per object of a project that has one. In a migration of its own; the other views stay
   as they are.

   | Column | |
   |---|---|
   | ProjectId | int |
   | BauteilId | built as in `revit.ObjectValues`; objects without one are left out |
   | MaterialId | built as in `revit.Materials`, e.g. MB-1761-01-BET-0031; global ones too, e.g. MP-9999-99-BET-0101; NULL for a material without a full ID |
   | MaterialName | the Materialname |
   | ChangedAtUtc, ChangedBy | the newest history row of the `ProjectObject` (key ProjectId\|ObjectEntityId); never the Changes column |

   - Global materials are in it because an object may be made of one. `revit.Materials` stays as it is.
   - Two objects with one Bauteil-ID give two rows. Unless both say the same, the add-in applies neither and says so, as
     with values.
   - No new grant: the role's SELECT on schema `revit` covers it. `$RevitViews` in `RevitLogin\_RevitLogin.ps1` and Studio's
     `RevitViewsTests` get it too.
   - Setting, changing and clearing an object's material, also by the import, has to move the change stamp.
   - Please say whether the Material-ID and Materialnummer values on objects, from before Studio's step 8b, are to stay.
     The add-in ignores them from then on. (Answered: none come any more, see "What the database session built".)
7. **For step 12 (asked for on 2026-11-25): the material's own fields, as more rows of `revit.MaterialValues`.** The view gives what Studio shows
   under "Values of group"; these are the fields above that. The columns stay (ProjectId, MaterialId, ParameterId, Value, ChangedAtUtc,
   ChangedBy). In a migration of its own, whose Down puts the present view back. No other view, no procedure and no grant changes.

   | Parameter | | Value |
   |---|---|---|
   | 00031 | IDE-KAT-Material-ID | as in `revit.Materials`, e.g. MB-1761-01-BET-0011 |
   | 00032 | IDE-KAT-Materialkategorie-ID | the category's code as it stands in the Material-ID, e.g. MB |
   | 00033 | MAT-KAT-Materialkategorie | the category's Name, e.g. Bauteil |
   | 00036 | IDE-KAT-Materialgruppen-ID | the group's code as it stands in the Material-ID, e.g. BET |
   | 00037 | MAT-KAT-Materialgruppe | the group's Name, as `revit.Materials` gives it, e.g. Beton |
   | 00038 | MAT-KAT-Materialnummer | MaterialNumber, plain: 11, not 0011 |
   | 00039 | MAT-KAT-Materialnummer2 | LegacyNumber, plain |
   | 00040 | MAT-BES-Materialname | Name, e.g. 11-Beton, C12/15 |
   | 00041 | MAT-BES-Materialname2 | AlternativeName |
   | 00042 | MAT-BES-Bezeichnung | Designation |
   | 00043 | MAT-BES-Bemerkung | Note, with its line breaks as stored |
   | 00044 | MAT-BES-Materialnorm | Norm, e.g. DIN 1045-2, EN 206-1 |

   - **Which materials:** those of `revit.Materials`. Global ones stay out.
   - **Which parameters:** found by their number, as Studio's `MaterialSheetsParser` and `ObjectMaterialService` do, and only those in
     `revit.Parameters`. ParameterIds differ per database.
   - **A blank field has no row.** The add-in then leaves what the model has.
   - **One row per material and parameter.** For these twelve only the own field counts: a row of `MaterialParameterValues` for one of them stays
     out, in case a group has it among its parameters. With two that differ, the add-in would apply neither.
   - **ChangedAtUtc, ChangedBy:** the newest history row of the Material, the same for all its fields; never the Changes column. Renaming a
     category or a group shows the new text at once; who and when stay as they were.
   - **The change stamp** has to move when one of these fields is changed or cleared, on the Materials page, by the import and by an approved
     proposal, and when the category or the group is renamed.
   - **Materialnummer2 and Materialname2** come whatever `LegacyMaterialNumbersEnabled` says. It only hides them in Studio, and changing it
     doesn't move the stamp.
   - **Please test** what Studio's `RevitViewsTests` test for the other values, and: every field; a blank one; a row in
     `MaterialParameterValues` for one of the twelve; a global material; a material without a full ID; a renamed category and group; the stamp; reading
     it as a user with only the `revit` grant; the migration's Down.
   - **Please say** whether these are the right fields, the migration's name, and what dev's 1761 has to walk with: how many materials have a
     Materialnummer2, a Materialname2, a Bezeichnung, a Bemerkung and a Materialnorm, and whether a Bemerkung has several lines. The workbook has 38,
     38, 15, 16 and 23.

Step 3 (linking a model to its project) needs only `revit.Projects`. It can start once the views exist
in dev MH_Database.

**The views.** The add-in reads exactly these columns, by name. Their order doesn't matter and extra
columns are ignored. Keep the views in step with Studio: when a migration changes a table they read,
update the view in the same migration. The add-in only notices if a view's own columns change.

A migration that renames or drops a column a view reads still succeeds, and only Revit would find out,
on every PC. Add a Studio test that selects from each view after migrating, so it fails in Studio
first. Making the views SCHEMABINDING would do the same job more bluntly.

| View | One row is | Columns |
|---|---|---|
| `revit.Projects` | one project | ProjectId, ProjectNumber, ProjectSubNumber, Name |
| `revit.Parameters` | one dictionary parameter that goes to Revit (one that has a RevitCategory) | ParameterId, Guid (NULL until the dictionary has it), RevitName, SetCode, GroupCode, Name, DataType, Unit (NULL for none), Target (Object or Material) |
| `revit.ParameterCategories` | one parameter on one Revit category | ParameterId, Category (Revit's BuiltInCategory name, e.g. OST_GenericModel) |
| `revit.ObjectValues` | one value on one object of a project | ProjectId, BauteilId, ParameterId, Value, ChangedAtUtc, ChangedBy |
| `revit.Materials` | one material of a project | ProjectId, MaterialId, MaterialName, MaterialGroupCode, MaterialGroup, SurfacePattern, CutPattern |
| `revit.MaterialValues` | one value on one material | ProjectId, MaterialId, ParameterId, Value, ChangedAtUtc, ChangedBy |
| `revit.ProjectStamps` | one project | ProjectId, Stamp (bigint) |

**What the views do.** The script explains each one in comments.
- **They build the IDs the way Studio does** (`BauteilIds.Format`, `MaterialIds.Format`):
  - BT-1101-01 comes from the object's catalog entry. Objects without all three parts are left out:
    Studio shows no ID for them, and today that's Schlitzrinne in 1761. The Bauteil_ID copies stored on
    project objects aren't used.
  - MB-1761-01-BET-0031 comes from the material, its category, group and project. A project's own
    materials only: the global MP products belong to no project.
- **RevitCategory becomes Revit's category name:** "Allgemeines Modell" → OST_GenericModel, "Material" →
  OST_Materials. When the dictionary gets a new Revit category, add a line to
  `revit.ParameterCategories`. Until then its parameters come through without a category, and the
  add-in reports them.
- **The change stamp and who/when come from AuditLogs:**
  - The stamp counts the history rows, plus the newest id, leaving out logins and settings. Counting
    catches a change that commits late with a lower id, which the newest id alone would miss. It's one
    stamp for all projects; it only tells the add-in when to read again.
  - Who/when is the newest history row for that value.
  - Never the Changes column: for user rows it holds password hashes (4 rows today).
- Objects point at their material through material parameters such as IDE-KAT-Material-ID, so an
  object's values include those references. (Until 2026-11-25. Since then an object's material is only in
  `ProjectObjects.MaterialId`, which `revit.ObjectMaterials` gives; see item 6.)

**Please confirm**
- Every change Studio saves to the tables above writes its AuditLogs row in the same transaction. A
  change saved without a history row would go unnoticed until something else changes.
- That includes MaterialCategories and MaterialGroups. Today the history has no rows for either, so
  renaming a material category or group code (which changes Material-IDs) wouldn't be noticed until
  something else changed.
- The history keys keep their shape ("13|69|61", "123|140").

**Bauteil-IDs must be unique within a project.** Yehia knows the 1761 case (the Bauteilunternummer is
the fix) and will say when it's done; until then it stays as it is. Built from the catalog, 1761 has no
duplicate today, because Schlitzrinne has no catalog numbers yet.

**Values:**
- Numbers use the invariant format with a dot ("0.3"), no thousands separator, in the parameter's
  Unit. Today's are like "56".
- Placeholders ([Modeller], [ET], -, TBT) come through as stored. The add-in skips them and lists them
  under "Not applied".
- No Boolean or Date values are stored yet. Tell me how they will be written, so they match the Excel.

**The read-only login (step 9, for the office).** What was asked for; what came of it is under "What the database session built".
- A role (name your call, e.g. `revit_reader`) with rights on schema `revit` only:
  `GRANT SELECT, EXECUTE ON SCHEMA::revit TO revit_reader`. That covers the views and the sign-in
  procedures.
- Nothing on dbo. The views read the tables through ownership chaining, because the script creates
  schema `revit` owned by dbo. Don't give the schema another owner: the tempting fix, a grant on dbo,
  would expose Users and the history.
- For the same reason: the role isn't in db_datareader (or any role with rights on dbo), and public and
  guest hold nothing on dbo.
- Check it once the role exists, as a user with only that grant:

  ```sql
  CREATE USER revit_check WITHOUT LOGIN;
  GRANT SELECT, EXECUTE ON SCHEMA::revit TO revit_check;
  EXECUTE AS USER = N'revit_check';
  SELECT COUNT_BIG(*) FROM revit.ObjectValues;  -- works
  SELECT COUNT_BIG(*) FROM dbo.Users;           -- must fail: permission denied
  REVERT;
  DROP USER revit_check;
  ```
- One SQL login in that role, with CONNECT on MH_Database and nothing else on the server. Use
  CHECK_EXPIRATION OFF: if this password expired, the link would stop on every PC at once. The password
  goes only into a text file of its own, for Yehia.
- **A script makes all of it in one go** (settled 2026-11-23, changed 2026-11-24): the role, the login, its user and the password.
  - It makes the password itself, long and random, of letters and digits only, so that it can be pasted without mistakes.
    It's not shown, not a parameter, not in the help, an error or a preview: Release Manager keeps every script's output in
    its logs.
  - It asks for the administrator's sign-in when it runs and stores it nowhere. Running it again changes nothing, unless
    it's told to make a new password.
  - It writes the password into a text file of its own, `C:\Yehia\DatabaseFiles\<login>_pass.txt` (or a path it's given), readable
    only by Yehia and administrators. Neither session opens that file. Yehia sends the password to each colleague in a private
    Teams message.
  - It doesn't write a connection file. The database session says what belongs in it: server and port, database, login,
    Encrypt, TrustServerCertificate, the master file.
  - **A check** signs in with the password from that text file and proves it: every view and both procedures answer;
    `dbo.Users`, the history and the three sign-in tables are refused. It never calls `CheckSignIn` with a real account. As every
    colleague has the password, this is what keeps the rest safe.
- **CADSQL can't be reached for now** (2026-11-24). Everything that doesn't need it is prepared and tried on a throwaway
  LocalDB database, so that only running it is left:
  - the script and the check, the checklist, and REVIT_LINK.md;
  - the values for the connection file, from Studio's own office settings: server and port, Encrypt, TrustServerCertificate;
  - a read-only script for the first contact: the version, whether SQL logins are allowed, whether Yehia's sign-in may make one,
    the collation of `Users.Username`, and which migrations are there.
- **From Egypt,** once it can be reached: from Yehia's PC through the VPN, against `CADSQL.mh-frlg.local,1433` like Studio's
  backups (BACKUP.md there), or it's said which steps can't and who in the office does them. Also ready: what the office's IT
  could run instead, without the password in it, as a login can be made from the password's hash
  (`CREATE LOGIN … WITH PASSWORD = 0x… HASHED`).
- **TheAdmin's first-start password** doesn't hold this up for now (2026-11-24). The database session only says whether it's
  still in use, on dev or in the office.
- **The scripts follow Release Manager's way,** so it could run them: named, typed parameters and no question when
  they're given; paths from `$PSScriptRoot`; a preview that writes nothing; an explicit confirmation before anything
  that counts; a check afterwards, as an exit code alone proves nothing; a failure is a non-zero exit code.
- **The order in the office:** back up; a Studio release through Release Manager's Database workflow (release.ps1, test,
  promote.ps1), whose first start on CADSQL applies the migrations (views, sign-in); the login script; the check; then
  the add-in's package. The sessions never connect to CADSQL: Yehia runs it.
- The login is shared, so whoever has its password can read every project, through the views only. That's accepted: it's
  every colleague who gets the Teams message.
- Tell me what Studio uses for Encrypt and TrustServerCertificate on CADSQL. (Answered: encrypted, the certificate not
  checked.) If the PCs don't trust
  CADSQL's certificate, the file needs `"trustServerCertificate": true`. Then the certificate isn't
  checked, and someone on the office network could pose as the server and catch the shared password.
  Better: give CADSQL a certificate the PCs trust (an internal CA is enough), so the file can say false.

**If AuditLogs grows large:** the views find the newest row per value through the existing index
IX_AuditLogs_EntityName_EntityKey. The stamp counts the whole history every minute, which is quick at
today's size. Tell me if the history gets trimmed or grows very large.

**Sign-in procedures (step 8; Studio accounts; the stored hash never leaves the server).** Settled on
2026-11-23, after reading Studio's `AuthService`, `LoginWindow` and `PasswordHashService`. Both go into schema
`revit`, so the schema grant covers them, and come with a Studio migration whose Down removes everything again.

| Procedure | Takes | Always gives exactly one row |
|---|---|---|
| `revit.GetSignInSalt` | `@Username nvarchar(256)` | Salt: base64 as stored (44 characters) |
| `revit.CheckSignIn` | `@Username nvarchar(256)`, `@Hash nvarchar(256)` (base64, 88 characters) | IsValid (bit); DisplayName (the user name where there is none) and Role, both NULL unless IsValid is 1; WaitSeconds (int): how long the next try is refused, 0 for not at all |

- **Who may sign in:** every active account. Role is only shown.
- **Names match as at Studio's sign-in:** by the database's own comparison of `Users.Username`, which looks like
  it ignores case and trailing spaces (the column has no collation of its own, although a comment in
  `AuthService` says case-sensitive). The add-in trims the name and leaves the password as typed, like Studio. A
  name longer than the column is an unknown name, never a cut one.
- **An unknown name, an inactive account and a wrong password look the same:** in the salt, in the row and in the
  waits.
  - The salt of an unknown or inactive name is made up but stable: an HMAC of the name with a secret, 32 bytes.
    A plain hash of the name could be checked by anyone. T-SQL has no HMAC, so nest HASHBYTES (SHA2_512).
  - Fold the name first, the way the lookup compares it. Otherwise "abc" and "ABC" get two salts, which gives away
    that there's no such account.
  - The secret is 32 random bytes the server makes in the migration (CRYPT_GEN_RANDOM), never in the source.
- **The hash is compared exactly.** Base64 tells upper from lower case and the database's comparison doesn't, so
  use a binary collation or compare bytes. NULL is wrong.
- **Waits after wrong tries,** per account (an unknown name counts the same way): from the fifth wrong try in a row
  30 seconds, then 1, 2 and at most 5 minutes. A right sign-in, or a day without a wrong try, clears it. Never a
  permanent lock: the login is shared, so anyone holding the connection file could otherwise lock every Studio
  user out, TheAdmin included.
  - During a wait a try is refused at once, unchecked, even with the right password, and doesn't lengthen it. No
    WAITFOR: it would hold connections.
  - Counting and checking are one step per account, under one lock. Otherwise tries sent at the same moment get
    through uncounted.
  - **Nobody may roll the count back.** Whoever holds the connection file can call the procedure inside a
    transaction of their own and roll back after each guess. So it refuses to run when `@@TRANCOUNT` isn't 0, sets
    IMPLICIT_TRANSACTIONS OFF, commits the count itself, and only then answers.
- **The log:** one row per try: when, which account (UserId; NULL for an unknown name), what came of it, and
  HOST_NAME(). Never the hash, and never the text of an unknown name, as people type their password there by
  mistake; where one must be told apart (the waits), keep its HMAC. Trim the log, e.g. to 90 days: anyone with the
  connection file can cause rows.
- **The secret, the waits and the log are tables in dbo,** not in schema `revit`: the role may read everything in
  `revit`. The procedures reach them through ownership chaining (dbo owns both schemas), so no EXECUTE AS, and the
  role needs no rights on dbo. No dynamic SQL.
- **None of it in AuditLogs:** a row there moves the change stamp, and every Revit would read its project again at
  each sign-in. `LastLoginAt` stays Studio's own. These counts are the only thing the link ever writes.
- **The hashing stays as it is.** The add-in computes `@Hash` like `PasswordHashService`: PBKDF2 over the password
  as UTF-8, HMAC-SHA512, 100,000 iterations, 32-byte salt, 64-byte hash, base64. A Studio test should pin those
  numbers: if they ever change, Revit's sign-in has to change in the same release (REVIT_LINK.md says so already).
- **Please test,** on a throwaway database:
  - right, wrong, unknown, inactive, NULL; the name in other case and with a trailing space; a hash with one
    letter in the other case; a name with no display name;
  - the wait: starts, refuses the right password, isn't lengthened, lifts, is cleared; tries at the same moment; a
    call inside a transaction and with IMPLICIT_TRANSACTIONS ON;
  - as a user with only `GRANT SELECT, EXECUTE ON SCHEMA::revit`: both procedures work; `dbo.Users`, the secret,
    the waits and the log are refused;
  - a sign-in leaves `revit.ProjectStamps` as it was; the migration's Down.
- **Please tell me:** the migration's name, once it's in dev MH_Database; how names compare there (the collation of
  `Users.Username`) and on CADSQL, if known; the waits, if they came out differently; and anything that doesn't fit.

## Questions

Answered by Yehia on 2026-11-19 (recorded above):
1. Sign-in: use the two procedures instead of reading `Users`, and put the file in ProgramData. Yes.
2. Encrypt the password in the file (DPAPI): maybe later. Plain text for now. (Since 2026-11-24 it isn't in the file: each
   person enters it once, and Windows keeps it encrypted.)
3. Project Number: `1761` without a sub-number, `1761-01` with one.
4. Who may sign in: every active Studio account.

Answered by Yehia on 2026-11-20:
5. Where the parameter definitions come from (step 4): Studio's master file, named in the connection
   file, as in the mockup.

Answered by the database session on 2026-11-24:
6. The read-only login's name: `revit_reader`.

Still open:
- Which colleagues get it first, and whether they all have an active Studio account.

## Status log

- **2026-11-19:** Read the brief, the database session's notes, the mockup and the 2027 code. Wrote
  this plan.
- **2026-11-19, later:** Yehia answered the four questions (recorded above) and asked for a mockup of
  the changed screens and the connection file. Step 1 comes later.
- **2026-11-19, afternoon:** Step 1 built: the Database panel with Test connection, the prototype removed,
  `MH.RevitTools.Database` and its tests (74 passing, one of them against LocalDB's master), the win-x64
  fix for SqlClient's files. Reviewed, fixes applied, deployed with a Debug build.
- **2026-11-19, walk:** Yehia walked step 1. Test connection connected in Revit 2027, so SqlClient loads and
  connects inside Revit. Step 1 is done.
- **2026-11-19, step 2:** Step 1 committed (ed6c17b) on the new branch `database-link` and pushed. Step 2
  built: the contract settled against MH_Database's real tables, `ILinkDatabase` with its SQL and fake
  versions, and tests, some reading 1761 through draft views. C# and security reviews: the stamp
  proposal was wrong (commit order), now fixed in the contract and the draft. The security points went
  into the contract, and the smaller code points were fixed. No change to Revit.Addin.2027, so nothing to
  deploy. Waiting for Yehia to pass the contract on.
- **2026-11-19, step 2 reworked:** Yehia answered:
  - no `revit` views, ever: query the tables;
  - build Bauteil-ID and Material-ID at lookup;
  - leave the duplicate Bauteil-ID until his fix;
  - use the change history.

  The reader now queries the tables, builds both IDs like Studio's `BauteilIds`/`MaterialIds`, and takes
  the stamp (row count and newest id) and who/when from AuditLogs, never its Changes column, which holds
  password hashes for user rows. The draft views are gone. "Needs from the database" rewritten: needed
  now are the GUID and Revit name per parameter, plus the master file.
  - The review of the rework found no blocker. Fixed:
    - "who changed it" now seeks the history's index per value instead of grouping the whole history;
    - change times come back marked as UTC;
    - a Material-ID with an empty code is built the way Studio builds it.
  - Added to "Please confirm": material category and group changes are audited, since none are today.
  - 104 tests pass, the real-data ones straight against the local MH_Database.
- **2026-11-19, views after all:** Yehia asked whether views would be better; I recommended them (Studio
  can change its tables without breaking Revit; the shared login sees only what Revit needs), and he
  chose them.
  - The direct queries became `RevitViews.sql`, the ready script for the database session.
  - The reader reads only the views again, and the category mapping moved into a view.
  - The tests run the script on stand-in tables, and over dev MH_Database through synonyms in a
    throwaway database. No `revit` object was created in MH_Database itself.
  - "Needs from the database" rewritten as a views request with a single schema grant.
  - 104 tests pass.
  - The security review of the script found nothing critical or high. Applied:
    - the script is safe to run again (schema only if missing, `CREATE OR ALTER`), with migration
      notes;
    - a test proves that a user with only the `revit` grant reads every view and is refused on the
      tables;
    - the request asks for a Studio test over the views, no db_datareader or public rights on dbo, a
      self-lifting sign-in lock, and the HMAC secret out of the role's reach.
  - 106 tests pass. MH_Database still has no `revit` objects.
- **2026-11-19, step 2 committed** on `database-link` and pushed. Yehia passes "Needs from the database"
  and `RevitViews.sql` to the database session.
- **2026-11-19, the database side is built.** The database session built the views, each parameter's
  GUID and Revit name, and the master file, and answered in `REVIT_LINK.md` (summed up under "What the
  database session built").
  - Checked here: the views in dev MH_Database have exactly the columns the add-in reads.
  - It also sent word that on dev the master file is now a local file, not the office one. Nothing here
    pointed at the office file.
  - Step 3 is next.
- **2026-11-19, step 3 built:** Database → Link project, the dialog from the mockup, and the link saved in
  the model (details under step 3).
  - The rule and everything the dialog says sit in the library (`Linking`), under tests. The Revit side
    only draws the dialog and saves the link.
  - The dialog was drawn off screen in six states and matches the mockup.
  - The tests' copy of the views now matches Studio's migration (GUID and Revit name). The tests over dev
    MH_Database read its real views instead of synonyms.
  - A C# review found nothing critical. Fixed:
    - a fault while showing the projects stays in the dialog instead of reaching Revit;
    - workshared models borrow Project Information before saving, with a plain message when they can't;
    - "1761 - Ulm" now reads as 1761 without a sub-number, and digits after another separator, as in
      "1761.01", mean no suggestion rather than a wrong one;
    - a search keeps projects that hold every word, so "1761 ulm" finds Ulm.
  - 187 tests pass. Deployed with a Debug build into both Revit 2027 add-in folders, with Revit closed.
- **2026-11-20, walk:** Yehia walked step 3: the model linked, and it's good. Step 3 is done.
  - Revit's Home screen hung on its Recent list that night. That wasn't the add-in: the PC's DNS failed
    for Autodesk's settings server, so Revit's online settings never came. Models → Open … still works.
  - The Link project button gets the mockup's link icon (the one in the dialog's header).
  - Yehia asked about the signed-in person shown in the mockup's ribbon: that comes with step 8.
- **2026-11-20, step 3 committed** (ee72e57). The push failed then, as github.com didn't resolve on this
  PC; it went up with step 4 on 2026-11-22.
- **2026-11-20, step 4 built:** Database → Check parameters, the dialog from the mockup, and Add from
  Studio's master file (details under step 4).
  - Yehia chose Studio's master file for the definitions. The connection file names it
    (`masterParameterFile`), and the dev connection file now does.
  - The check, the master-file reader and everything the dialog says sit in the library (`Parameters`),
    under tests. The Revit side reads the model's bindings, binds and draws.
  - The dialog was drawn off screen in four states and matches the mockup.
  - A C# review found nothing critical. Fixed:
    - Revit's own shared parameter file setting is put back safely;
    - one refused parameter no longer stops the others;
    - extending a binding keeps its other categories and palette group;
    - a failed Add can be tried again;
    - each Revit gets its own copy of the master file.
  - 247 tests pass. Deployed with a Debug build, with Revit closed.
  - The dev master file isn't there yet: Studio writes it the next time it starts on dev.
- **2026-11-22, walk:** Yehia walked Check parameters (step 4): it works. Studio had written the dev master
  file. He asked for:
  - a button that replaces Other GUID parameters after asking: **Replace N…** (step 4);
  - the mockup's Database panel: the big Database button with three rows under it, the last one for the
    signed-in person.

  Both are built. Replace was drawn off screen before and after, with its question checked; the panel's
  icons were checked on a light and a dark ribbon. LinkProjectIcon.png went, as the big Database button
  now opens the Link dialog.
  - A C# review of Replace found nothing critical. Fixed:
    - parameters used in schedules or view filters are left alone;
    - a swap Revit would delete more with is refused;
    - the stale-parameter guard compares the GUID too;
    - the dialog can't be clicked again while Replace asks its question;
    - an unexpected error undoes only its own swap;
    - a missing ribbon icon can't hide the tab.
  - 253 tests pass.
- **2026-11-22, later:** committed (00b0fb7) and pushed together with step 3. Yehia tried both: they work.
  He asked for two more:
  - the Database button to show the link: the project number under "Database", and a green dot on the
    icon (step 3);
  - Replace to take parameters that schedules or view filters use as well, after a second warning that
    he confirms (step 4).

  Both are built and deployed with Revit closed, but not walked yet. The button's text and the warning's
  wording sit in the library under tests (`DatabaseButtonFace`, `ReplaceUses`, `ParameterCheckTexts`). The
  Revit side follows the active model and finds the schedules and filters (`DatabaseButton`,
  `ParameterUseScan`).
  - The first Replace question now also says that tags show the new parameters only once their tag
    families use the master file's.
  - A C# review found nothing critical. Fixed:
    - after a model closes, the button looks at the active model once Revit is idle;
    - when Revit can't check, the answers don't claim that schedules use the parameters;
    - any failure of the scan counts every parameter as used.
  - 275 tests pass.
  - Only Revit can show two things: whether Undo of a link turns the button back (Revit has to report
    Project Information as changed), and what Revit does to a view filter whose rule loses its
    parameter.
- **2026-11-22, walk:** the Database button works. Replace replaced nothing: Revit said it would delete
  other elements with every parameter, and the add-in refused each swap. Yehia asked to see those
  elements by name in a list, with a button that deletes them all and then replaces the parameters.
  - Built (step 4, "When Revit can only delete…"): the add-in tries each deletion in a change it always
    undoes, lists what Revit deleted with it, and deletes exactly those after the red button.
  - The list was drawn off screen with made-up elements, short and long.
  - The answer type for Replace's questions is now `ReplaceChoice`, shared by the second question and
    the list.
  - A C# review found nothing critical. Fixed:
    - an element that two parameters take is listed once, naming both, and neither counts as going on
      its own;
    - Revit's messages about the trial are cleared with it, never shown;
    - a failed rollback can't hide the error before it.
  - Not known until Revit shows it: whether the trial borrows elements in a workshared model. Replace
    itself borrows them anyway; after a Cancel, Relinquish All Mine gives them back.
  - 283 tests pass. Deployed with Revit closed.
- **2026-11-22, committed** (abe28f8) and pushed. Yehia tried the list: the elements had no name. He
  asked for a button that makes a view with just those elements. Built: "Show them in a new view", and a
  line under each name saying where the element is (its view, or no view at all). Drawn off screen with
  made-up elements: some model elements, and none.
  - A C# review found nothing critical. Fixed: the new view has no view template and shows the elements'
    categories and worksets, so isolating can't leave it empty; a failed zoom no longer says the view
    didn't open. (Its "ActiveView switches only later" point was wrong: that's RequestViewChange.)
  - 285 tests pass. Deployed with Revit closed.
  - Yehia's answer: "everything is good", without saying what the list or the new view showed. Committed
    and pushed with this entry.
  - He also asked whether the pane's changes come from Studio's history and activity, and so are the same
    across the database: yes. The stamp and each value's who/when come from AuditLogs in MH_Database,
    which every PC reads. The add-in never writes to the database, so what Revit applies doesn't appear
    in Studio's history.
- **2026-11-22, step 5 built:** Yehia wrote "commence step 6"; asked whether he meant Apply (step 6) or
  the pane (step 5), he chose the pane. Built as described under step 5: the docked MH Database pane, the
  check on its own thread, the last pull kept per person, and the comparison of model and database
  ("changes" are the database's values the model doesn't have yet, as in the mockup). The Database button
  now shows and hides the pane, and its dot shows the pane's state.
  - The check, the kept pull, the settings, the comparison and every word the pane says sit in the
    library (`Checking`, `Changes`) under tests. The Revit side reads the model, compares one value at a
    time and draws the pane (`DatabaseLink\Pane`).
  - The pane was drawn off screen in seven states (changes, up to date, not reachable with the last pull,
    not linked, no connection file, project not found, no model) and looks like the mockup, which has no
    picture of the last two. The button's icons were drawn with each dot on a light and a dark ribbon.
  - A C# review, reading the code only, found nothing critical and no place where another thread touches
    Revit. Fixed (the one high point and four of the five medium ones, plus some low ones):
    - after switching to another database, a comparison of the first one's values could stay on screen:
      data versions now never repeat;
    - a kept pull now names the project number it was read for, is used only for that project, and goes
      when the database no longer has the project or its id means another one;
    - one value Revit can't read no longer stops the whole comparison: it's named under the changes, and
      a failed comparison says so in the pane;
    - an external event Revit turns down is asked for again a second later;
    - the comparison remembers elements and converted values, so big models compare faster;
    - a parameter that sits on the element's type counts, and text is compared without stray spaces.
  - Not changed, the fifth medium point: between full reads the check doesn't look at the project's number
    again, so it notices a renumbering only when that moves the stamp. In Yehia's walk on 2026-11-23 the
    pane did notice one. Not changed either (low): a damaged settings file loses its other keys.
  - 356 tests pass. Deployed with Revit closed; not walked, not committed.
- **2026-11-23, walk item 1, so far:** Yehia linked the model, typed BT-1101-01 into an element, and the
  Database button opened the pane on the right. "Click Database" had read like opening Studio; it's the
  ribbon's Database button. Asked what the pane showed, Yehia answered "Perfect commit and push". That
  doesn't say whether it listed the changes, so item 1 stays open. Committed and pushed (1721f93).
  - Corrected the same day: 1721f93's version of this file said that the pane had listed the changes, that
    Yehia had tried the new view, and that Yehia had agreed to the dot's states. None of that was said.
- **2026-11-23, step 5 walked:** Yehia walked around the pane. In Yehia's words: "the buttons meanings are
  good and are reflected". Yehia changed a project's number and name in Studio: the pane turned red, said the
  project had changed, and Change project… put it right. None of the elements Replace would delete is one a
  view can show, so "Show them in a new view" stayed greyed out and couldn't be tried.
- **2026-11-23, step 6 built:** Apply and "Not applied", as described under step 6.
  - The brief fixes that the existing writers are reused, so the Excel import's value-writing loop moved to
    `Helpers\ElementValueWriter` and the link uses the same one.
  - What to write, what to leave out and every word about it sit in the library (`ChangeFinder`,
    `NotApplied`, `ApplyPlan`, `ApplyResult`, `ApplyTexts`) under tests. The Revit side (`DatabaseLink\Apply`)
    borrows, writes and counts.
  - Found on the way: where several objects share a Bauteil_ID and disagree on a value, the link now applies
    neither.
  - The pane was drawn off screen with Apply, after an Apply, with several kinds of "Not applied", while the
    database isn't reachable, and after a refusal.
  - A C# review, reading the code only, found nothing critical or high and no difference in the Excel route.
    Fixed: Apply now writes only into the elements the list named (it went by Bauteil_ID before); the plan
    itself refuses placeholders, identity parameters and parameters of the wrong kind; the ribbon says what
    happened when there was nothing left to apply. Left as it is (low): elements borrowed for an Apply that
    Revit then refuses stay borrowed until the next synchronization.
  - 359 tests pass. The 26 tests that need LocalDB weren't run: this session couldn't reach it. No SQL changed.
- **2026-11-23, step 6 walked:** in Yehia's words, "I tested all yours steps and its working smoothly".
  Committed and pushed with this entry.
- **2026-11-23, Bauteilnummer and Bauteilunternummer:** at Yehia's word the link now fills both; step 6 as
  walked left them out like the Excel import. 360 tests pass (the 26 that need LocalDB weren't run). Not tried
  in Revit.
- **2026-11-23, step 7 built:** Compare with Excel, as described under step 7. The comparison and its words sit in
  the library (`Comparing`) under tests; the add-in reads the workbook, converts the values as Revit does and
  draws the window (`DatabaseLink\Compare`). It was run outside Revit on a copy of the workbook and the kept pull,
  and the window was drawn off screen. The findings are under step 7.
  - A C# review, reading the code only, found nothing critical. Fixed: the headline said "Identical" although the
    Excel reader had left rows out; a Parameter-ID copied to a second column took that column for the first one;
    a fixed tolerance hid differences in long whole numbers and in very small ones; a material the Excel reader
    can't create wasn't compared by name and group; the global materials and the fill patterns are told more
    strictly; messages came up under the wait cursor. Two points weren't real: both sides skip the same four
    placeholders, and the Excel import itself ignores the case of a Bauteil_ID.
  - Checked with a faulty copy of the workbook, read by the real Excel reader: an object given twice, a row without
    an ID, a material without a group and one without a name were each listed, and two columns with one
    Parameter-ID were kept apart.
  - 400 tests pass. The 26 that need LocalDB weren't run. Deployed to both add-in folders on Yehia's PC, together
    with the Bauteilnummer change. Not tried in Revit, so Revit's own unit conversion hasn't been seen at work
    here yet.
- **2026-11-23, after step 7:** Yehia leaves the values the views don't give for now (item 5 of "Needs from the
  database"), and takes the request for the sign-in procedures (item 3) to the database session. Its contract
  above was made exact for that, against Studio's own sign-in code, which was only read.
- **2026-11-23, step 7 walked:** in Yehia's words, for steps 7 and 8, "It works commit and push step 7 and 8".
  Committed with this entry.
- **2026-11-23, the sign-in procedures are built** by the database session (summed up under "What the database
  session built").
- **2026-11-23, step 8 built:** the sign-in, as described under step 8. The hashing, the tries, who is signed in and
  every word sit in the library (`SigningIn`) under tests; the Revit side (`DatabaseLink\SignIn`) asks before anything
  reads the database and shows who is signed in.
  - The hash was checked against Studio's own pinned test value, made again here with OpenSSL.
  - The window was driven off screen with a made-up database (refused, waiting and the wait lifting, cancelled while
    trying, not reachable, signed in) and drawn in eight states; the pane was drawn in its two new ones.
  - A C# and a security review, reading the code only, found nothing critical or high. Fixed: the sign-in's
    connection is always encrypted; signing out takes the changes list and Apply away at once instead of with the
    next check; an answer that was still being read when someone signed out isn't shown; a name from the database is
    shown on one line. Left as it is: after the window is closed, the database itself still refuses a name that has
    to wait, and says so; a connection file that changes while Revit runs counts from the next check.
  - 470 tests pass. The 32 that need LocalDB weren't run: this session can't reach it. Six of them are new and run the
    procedures' copy on a throwaway database.
  - Deployed to both add-in folders on Yehia's PC, with step 7. Not tried in Revit, and not against the real
    procedures: this session doesn't write to dev MH_Database, and a try is written down there.
- **2026-11-23, step 8 walked:** in Yehia's words, for steps 7 and 8, "It works commit and push step 7 and 8".
  Committed and pushed with this entry.
- **2026-11-23, a question of Yehia's:** does the link assign materials to objects? No. It only writes values into
  parameters of materials that are already in the model, found by their Materialname. It doesn't create materials,
  doesn't assign them to elements and doesn't set fill patterns. The Excel import's Automate Material Creation does all
  three (it assigns through the element's "Material" parameter). The views hold each material's name, group and fill
  patterns, except global materials. I also said they hold each object's material (BT-1101-01: MB-1761-01-BET-0031);
  that's only an old copy, found while planning: see step 11.
- **2026-11-23, materials planned:** Yehia: "Yes, plan it". Steps 10 (create) and 11 (assign) are written down above, and
  item 6 of "Needs from the database" asks for the view step 11 needs. Creating comes first: it needs nothing from the
  database session, and assigning then finds every material. Nothing is built.
- **2026-11-23, step 9 first:** Yehia takes the request for the login and the office's connection file (item 4) to the
  database session. The add-in's half is built meanwhile ("Yes, build it"): `Scripts\ConnectionFile`, as described under
  step 9.
  - Finished on 2026-11-24. 87 tests on throwaway folders pass in Windows PowerShell 5.1 and in PowerShell 7
    (`Test-ConnectionFile.ps1`), and the add-in's own reader takes the installed file.
  - A security review, reading the code only, found two high points, both about a folder someone prepared before: it was
    locked in place, which a program that had it open before could undo. Now a new one takes its place. Also fixed:
    the file is read once, and what was checked is what's written; the hand-over folder is closed from its first moment and
    only cleared of what the script puts there; its place is checked more strictly. Found by the tests: Windows' own JSON
    reader takes a file that was cut off; started from PowerShell 7, Windows PowerShell can't load `Get-Acl`.
  - Not tried: as administrator, which is what gives the folder to the administrators, and on another PC. That's the walk.
- **2026-11-24, security measures lifted for now:** Yehia is in Egypt, the office is in Germany, and the link is to be published
  so that colleagues can install it. The last decision above says what's lifted and what stays. The installer that locks the
  file is kept for later; the connection file goes along with the add-in instead.
  - Built: `ConnectionFile.ReadFirst` and `ConnectionFilePlaces` in the library, `ConnectionFiles` in the add-in, and the 2027
    project's Release build takes the file along. No script is changed, and 2024 and 2026 aren't touched. `database.json` is in
    `.gitignore` now. 496 tests pass; the 32 that need LocalDB weren't run, and no SQL changed.
  - A C# review, reading the code only, found nothing critical. Fixed: an old file left in the build output is always deleted
    (its high point; in the trial the build removed it by itself); a developer's file that Windows won't let Revit look at
    counts as there, instead of leading on to the office's. Left as it is: a file that OneDrive has to fetch first can hold up
    a command for a moment, the first time.
  - Tried on a throwaway copy with a made-up file: Release Manager's own build command (with the project reference to
    `MH.RevitTools.Database`, which it hadn't built before), `New-Package.ps1` with the ZIP, `Publish-ToShare.ps1` into a scratch
    folder, `Install.ps1` into scratch folders; the built library then reads the file from both places. Nothing was deployed or
    published.
  - Not tried in Revit, and not with the real file, which doesn't exist yet. Also not tried: an update by Check for Updates from
    5.2.2.0, which leaves that version's `deps.json` and `runtimes` folder beside the new DLLs. One colleague first (the walk).
- **2026-11-24, the password is entered in Revit:** the last decision above. Built as described under step 9 and "The connection
  file"; what the entry before says about a Release build and `C:\Yehia\ProjectsApps\Revit.Addin\database.json` is gone again.
  - In the library, under tests: connections without a password, `SavedPasswords` (with Windows' own encryption, tried for real),
    what counts as refused, `PasswordWatch`, the check, every word. In the add-in: the window, `SignInGate`, and every command and
    the pane go through it. No script and no SQL changed, and no package was added: Windows' encryption comes with SqlClient's.
  - A C# and a security review, reading the code only, found nothing critical. Fixed: a sign-out that came late could have signed
    out someone who had just signed in with the new password (the C# review's high point), so a sign-in now holds only for the
    password it was made with; a password refused in Link, Check parameters or Compare isn't used again either; a server name with
    look-alike letters would have got the saved password; the test of the shipped file allows its six entries and nothing else.
    Left as they are: whoever can edit the Teams copy can name another server, and Revit then asks there for the password
    (they could replace the DLLs, too); a PC's own file with a wrong password is still tried at every check.
  - 575 tests pass; the 32 that need LocalDB weren't run. The password window and the pane's new cards were driven and drawn off
    screen, and the sign-in window with a refused database password. Tried again on a throwaway copy: Release Manager's build
    command, package, ZIP, publishing and installing into scratch folders; the built library then asks for the password with
    the published file, keeps it and finds it in the next session.
  - Deployed to both add-in folders on Yehia's PC (Debug). Not tried in Revit, and not with the real login, which doesn't exist yet.
- **2026-11-24, CADSQL can't be reached for now:** Yehia takes a new prompt for item 4 to the database session, which replaces the
  earlier ones: prepare everything that doesn't need the server.
- **2026-11-24, the database session's answer to item 4:** prepared, tested and pushed; summed up under "What the database session
  built". Asked for and done here: it's recorded, the add-in fits its two errors (nothing had to change, three tests were
  added), and step 9's walk says what can be walked without the office. The reason never to try a refused password again is
  another one now, as the login has no lockout.
- **2026-11-24, the connection file is built into the add-in:** the last decision above. The Teams folder and the copy beside the
  DLL, as the entries of this day before describe them, are gone again.
  - Built: `Revit.Addin.2027\database.json` is a resource inside `Revit.Addin.2027.dll`; `ConnectionFile.ReadFirst` takes the PC's own
    file or, without one, that. `ConnectionFilePlaces` is gone. The pane's card for a missing file says now that the connection
    file can't be used, and why; a mistake in a PC's own file says that it counts instead of the add-in's. No script, no SQL and
    no package changed.
  - 571 tests pass; the 32 that need LocalDB weren't run. The pane's card, the sign-in and the password window were drawn off
    screen.
  - Tried on a throwaway copy: Release Manager's build command, the package with its ZIP, publishing into a scratch folder, and
    what Check for Updates copies, over an older version's folder. Each time the add-in gives its connection with the six
    values, and no separate file comes along. The same with the deployed one.
  - A C# and a security review, reading the code only, found nothing critical or high. Left as they are: a folder in the place of
    the PC's own file is called "No connection file"; and a PC's own file, which any user of that PC could put there, can name
    another server, and Revit then asks for the password there, naming the server.
  - Deployed to both add-in folders on Yehia's PC (Debug); the two copies from the same afternoon, which nothing reads any more,
    were removed there. Not tried in Revit.
- **2026-11-24, updating from 5.2.2.0** (Yehia asked whether Check for Updates brings the connection file): yes, inside the DLL. Its
  copying, done on a scratch copy of the published 5.2.2.0, adds `MH.RevitTools.Database.dll` and SqlClient's native DLL and replaces
  SqlClient's placeholder. That version's `deps.json` stays and doesn't name the library, but that does no harm: Revit 2027's loader
  (`AddInLoader.dll`, read, not run) looks in the add-in's folder first and only then by `deps.json`, and our manifest leaves the
  add-in in Revit's own context. The manifest is the published one, unchanged.
  - The antivirus removed the scratch copy's `Revit.Addin.2027.dll` (Debug) when a throwaway program loaded it from there
    (0x800700E1). The deployed ones, the same file, weren't touched, and Release copies had loaded before. Nothing was retried
    or excluded. So one colleague first, also for the antivirus.
- **2026-11-24, step 9 committed, not walked:** Yehia: "I will try the walk once the office can be reached thats fine" and "Commit
  and push step9". Yehia also asked whether the password has to be entered again with each update: no (see "The connection file",
  The password). Committed and pushed with this entry.
- **2026-11-24, step 10 built** ("we proceed to step 10"), as described there. In the library, under tests: what's missing
  (`ChangeFinder`), what Create makes (`CreationPlan`), the patterns (`MaterialPattern`) in the list and in Apply, every word. In the
  add-in: `MaterialCreator`, `RevitPatterns`, `ValueSetter` (taken out of `DatabaseApplier`, unchanged), the pane's section. The one
  change to the Excel import's code: its loop body is a method of its own now (`MaterialCreation.Create`), in 2027 only.
  - Over the kept pull of dev's 1761, without a database: 38 materials missing in an empty model, all 38 planned with 109 values;
    three have patterns (_Waben or Solid; three Ortbeton names).
  - 621 tests pass; the 32 that need LocalDB weren't run. Compiled as Debug and as Release. The pane's new states were drawn off
    screen.
  - A C# review, reading the code only, found nothing critical or high. Fixed: IDs that differ only in spaces count as one in the
    list too; a pattern that was there already isn't counted as applied; the sections' titles tell a screen reader whether
    they're open. Left as it is: an exception that isn't a refusal of Revit's stops the whole Create, as with Apply.
  - Not deployed while Yehia's Revit was open, and not tried in Revit: what Revit does with the look, the structural properties and
    the patterns hasn't been seen at work from here.
- **2026-11-24, step 10 installed:** Yehia closed Revit; deployed (Debug) to both add-in folders. Not walked. Yehia takes the
  request for step 11's view (item 6) to the database session; it was checked against Studio's own code, which was only read.
- **2026-11-24, Check for Updates looked at** (Yehia asked whether the tool's update functionality behaves correctly). Nothing is
  changed: `CheckPluginUpdate.cs` is the same in 2024, 2026 and 2027.
  - Its script was run on made-up files. With Revit closed it brings every DLL and the manifest, also over a path with "ü".
  - With two DLLs in use, as by a Revit that's still open, xcopy stops at the first one: the files before it are new, the rest is
    old, the manifest is new, and it says "Update Complete.". Without the Teams folder nothing is copied, with the same words.
  - It waits 10 s instead of until Revit has closed. In the last 20 journals here that show it, Revit went on for 3 to 5 s
    after the add-in's shutdown, 10 to 60 s after the exit command; when in between the script starts, they don't say. A second Revit
    window keeps the DLLs in use anyway.
  - It offers any other number as an update, an older one too. Its first question is the wrong way round: "Have you synced …?"
    Yes opens Teams and stops, No goes on. `%TEMP%\run_plugin_update.bat` is the same file for every Revit year. The manifest only
    goes to ProgramData, and Revit 2027 reads the person's own in `%APPDATA%`; it hasn't changed since 5.2.2.0.
  - `Update-FromShare.ps1` does it right, and says that the ribbon hands off to it; the ribbon doesn't.
  - The version that's installed does the copying, so a correction helps from the update after it.
  - Update Family Files was only read: nothing wrong with the files it writes. "Cancelled after updating 3 of 3 files" counts the
    processed ones, not those found; and old backup folders are only left out directly under the chosen folder.
- **2026-11-24, Check for Updates corrected** (Yehia: "Yes fix it for 2027 and copy the fixed version for 2026 and 2024").
  `Services\PluginUpdate.cs` (new, without Revit) and `Commands\CheckPluginUpdate.cs` are the same in all three years but for the year;
  a test sees to it. ProjectsApp's copy of 2024 isn't changed, and neither are the scripts.
  - It offers only newer versions, compared as numbers, and only when the Teams folder is complete (the DLL there has the version of
    `version.txt`). Without the Teams folder it offers to open it; otherwise it asks nothing first.
  - The whole payload is copied to `%LOCALAPPDATA%\MH\RevitTools\Update\<year>` at once, by content, without the Mark of the Web. The
    script installs it once Revit has closed: it waits until no DLL is in use, however many windows are open, saves the installed
    files, replaces them, checks that all arrived, and refreshes the manifest, the person's own too if there is one. If anything goes
    wrong, the files from before go back, and it says NOT UPDATED. Plain cmd and the person's own rights, as before; whoever may not
    write there is told so, and an administrator installs it with `Install.cmd`.
  - One at a time: with the update accepted in two windows, the second script ends, and Check for Updates says "An update is already
    waiting". The window can be closed at any time: the add-in's own DLLs come last, so it's the old version until everything
    else is new, and the update is offered again.
  - Two reviews, reading the code only (C# and security). Fixed after them: two scripts at once, a copy interrupted halfway, a window
    left open that stopped the next update, a Teams folder that can't be read, a manifest that can't be saved first, the raw text of
    `version.txt` in the script. Left as it is: with the DLLs free it installs at once, but while one can't be replaced, for whatever
    reason, it goes on waiting as long as any Revit runs, of any year or person; of the published files only the add-in's DLL is
    compared with `version.txt`, so another one that OneDrive hasn't brought yet isn't noticed; after a crash of Revit the update
    has to be accepted again.
  - Tested with the real script on folders of its own (with ü, & and % in the path): closed, in use, waiting as long as Revit runs, two
    at once, interrupted, a file that can't be replaced or is gone, the manifests, a window left open, twice. Found by that:
    `robocopy` takes a file of the same size and time for the same one, so the copies have a time of their own; and another `find`, like
    Git's, can come before Windows' own, so the script uses Windows' own only.
  - **The antivirus (G DATA) stopped one run:** with the real Debug payload, in scratch under `Temp`, it took `Revit.Addin.2027.dll` for
    unwanted software when `robocopy` read the copy, and removed that copy (22:12). The script said NOT UPDATED, and the files from
    before stayed. The DLL in both add-in folders and in `bin`, the same file, wasn't touched. Not retried, nothing excluded. Since
    then the script also counts the copied files first. Whether G DATA minds the real way (Revit, `%LOCALAPPDATA%`, `ProgramData`)
    isn't known: it has to be tried on Yehia's PC, and then by one colleague first.
  - 690 tests pass; the 32 that need LocalDB weren't run. All three years compile; 2027 is deployed (Debug) to both add-in folders on
    Yehia's PC, 2024 and 2026 aren't. Not tried in Revit, and not committed.
- **2026-11-25, set up so that Yehia can walk a real update** ("Yes set it up"). The Teams folder publishes 5.2.2.0, as the PC had, so
  today's code was compiled as 5.2.1.0, with a version file outside the repo, and its DLL put into both add-in folders on Yehia's
  PC. Nothing in the repo says 5.2.1.0, and nothing is written to the Teams folder. The updater, asked outside Revit, offers 5.2.1.0 →
  5.2.2.0. G DATA left the DLL alone. After the walk the PC has the published 5.2.2.0, without the database link, until
  today's build is put back (Debug, Revit closed). Walk:
  1. Revit 2027, any model, Parameter Tools → Functionality → Check for Updates: "Update from 5.2.1.0 to 5.2.2.0?" Yes: "Version 5.2.2.0 is
     ready to install". Clicked again, it says the same.
  2. A second Revit 2027, then close the first: the small window waits. In the second one, Check for Updates says "An update is
     already waiting".
  3. Close the second one too: "Installing…", then "Updated to 5.2.2.0. You can start Revit 2027 again." Whether G DATA minds.
- **2026-11-25, a real update walked:** Yehia: "It updated, looking good". Yehia didn't say which parts of the walk that covers, nor
  anything of G DATA. On the PC, all 63 files are the published ones, both manifests too, and of the update only the script, the lock,
  the log and the copied manifest are left. From the script's start to its end it took 8 s. The PC now has the published 5.2.2.0;
  the link's build isn't put back yet.
- **2026-11-25, a release folder without the database link** (Yehia: "I need you to jsut make a build that I can release without the
  database link, I will release it through release manager as 5.3.0.0"):
  `C:\Users\Y.Abdelazziz\source\repos\Revit.Addin.MultiVersion.Release`, a git worktree on the new branch `release-5.3.0`. It's
  master (928b8c2, the released 5.2.2.0) plus the corrected Check for Updates in 2024, 2026 and 2027, and nothing else. Nothing there
  is committed, and it still says 5.2.2.0: raising the version and releasing are Yehia's. Release Manager needs that folder under
  Locations; it isn't changed itself.
  - A folder of its own, because this one has the link in it, uncommitted work, and old files in its `bin\Release`, which a release
    would take along.
  - Built as Release Manager does, with its isolation, so nothing was installed. 2026: 82 files, 2027: 63, the same names as published,
    and only the add-in's DLL is different. 2024: 90, the same but for that DLL; the 78 more in the Teams folder are Revit's own, left
    in this folder's `bin\Release` since April 2026, and no built DLL refers to them but to RevitAPI and RevitAPIUI, which Revit
    has. No DLL has anything of the link: no type, no library, no `database.json`.
  - The old button "Populate DB Parameters" is there as in 5.2.2.0, in all three years. In `database-link` 2027's is deleted.
  - The changelog workbooks say the same as the ones in the Teams folder; 2027's there was saved again on 2026-11-24.
  - After the release, on Yehia's word: commit there, take it over to master, and merge master into `database-link`. Until then the
    link's builds say 5.2.2.0, and Check for Updates offers them 5.3.0.0, which has no link.
- **2026-11-25, 5.3.0.0 released** by Yehia with Release Manager, for 2027 (01:58), 2026 (02:04) and 2024 (02:05), from the release
  folder, all nine steps each. Read back: all built files (63, 82, 90) are in the Teams folder as built, every DLL says 5.3.0.0, has the
  new updater and nothing of the link. The ZIPs are in `C:\Yehia\ProjectsApps\Revit.Addin`. Yehia's PC has 5.3.0.0 installed in all
  three years; the link's build isn't back on it.
  - Yehia: "Its set, now let us change this back to the old location". Release Manager's Locations are Yehia's to set. So that
    this folder doesn't say 5.2.2.0 any more, the released version number and changelog (`version.txt`, `AssemblyInfo.cs`,
    `version-docs.xlsx`, all three years) were taken over into it. Built from here, 2027 has the link.
  - Nothing is committed, here or in the release folder. **The release folder's changes are the only copy of what was released,** so
    it stays until they're committed.
  - The link's build is back on Yehia's PC ("Yes, put it back"): 2027, Debug, says 5.3.0.0; all 58 files in both add-in folders are the
    built ones. 2024 and 2026 have the released 5.3.0.0. Steps 9 and 10 can be walked with it.
- **2026-11-25, step 10 walked and committed:** Yehia: "I tried step 10, works good..you can commit and push the database session is
  almsot done with its step 11 part". Committed and pushed with this entry on `database-link`: the updater, the version number, step 10.
  The released code is committed and pushed on `release-5.3.0`, so the release folder isn't its only copy any more. Then master took
  it over ("Yes, to master"): e15e78a, two commits forward, pushed. It merges into `database-link` without a difference, as the
  same changes are here already.
- **2026-11-25, step 11 built** (the database session's answer to item 6, which Yehia brought: "Build step 11, with your copy of the view
  and the stand-in table"), as described there. Nothing didn't fit. The copy of the view here is the migration's, word for word.
  - Over the real workbook (a copy) and dev's kept pull of 1761, with BT-1101-01's material added by hand as the pull is older than
    the view: "Objects made of another material: BT-1101-01 · Material: Excel “31-Beton, C35/45”, database “13-Beton, C20/25”"; the
    workbook's Material-ID and Materialnummer are "By design". The pane's new lines were drawn off screen.
  - 726 tests pass. The 35 that need LocalDB, three of them new, weren't run from here. The library is built for both frameworks, 2027
    compiled; 2024 and 2026 aren't touched.
  - A C# review, reading the code only, found nothing critical. Fixed: Apply also compares the Material-ID with what was listed, as
    two materials can share a name; one odd element can't stop Apply; a material is looked up once per name; the data can't be
    put together without what the objects are made of. Left as it is: without the view the whole read fails, as the database
    session said.
  - Deployed (Debug) to both add-in folders on Yehia's PC, Revit closed. Not tried in Revit. Yehia walks it on 2026-11-26 ("Let us do
    the walk tomorrow then").
  - Revit has read the view: the pull it kept at 02:21 UTC has BT-1101-01's material, who set it and when.
- **2026-11-25, which material parameters are filled** (Yehia: "only the parameters that live under "Values of group (BET for exmaple)" in
  Studio that gets its values added, Materil group, category, Material standard materila number materila numbner2 and name 2 do not
  and Im not sure if the revit appearnce gets set to"). Read only:
  - Right: those are the material's own fields in Studio, which `revit.MaterialValues` doesn't give ("Needs from the database", item 5).
    The Excel import fills them, as they're columns of its sheet.
  - The Revit appearance is Studio's pair of fill patterns (Oberfläche-Ansicht, Oberfläche-Schnitt), which the link sets since step 10
    where the model has patterns of those names. In dev's 1761 three materials have one: 11-Beton (_Waben, Ortbeton - bewehrt), 12-Beton
    (Solid, Ortbeton - bewehrt2) and 13-Beton (Solid, Ortbeton - unbewehrt); the other 35 have none. Colours and the render look
    aren't in the database. Darstellung-ID, its Bezeichnung and the two pattern names are parameters of Generic Models in the
    dictionary, and have no values.
- **2026-11-25, step 12 planned** (Yehia, asked whether the link should fill the material's own fields too: "Yes it should, so plan it and
  give me the prompt for the database session"). Nothing is built, and step 11 stays as it's installed. Gone through, read only:
  - The pane's list, Apply, Create… and Compare with Excel take such rows as they are. One thing doesn't fit yet: texts are compared letter
    by letter, so a note of several lines could be listed for its line breaks alone.
  - Of the 472 "Material values only in the Excel" in the comparison of the day (the real workbook, a copy, and dev's kept pull), 358 are these fields,
    and 114 are Projekt-ID, Projektnummer and Projektunternummer of the 38 materials.
- **2026-11-25, four fixes of the Excel import** (not the link; three reports of colleagues on 5.3.0.0, which Yehia brought: "can you
  investigate this? Then we can continue on the database link", then "Yes, fix them"). Made in the release folder (branch `release-5.3.0`)
  for 2024, 2026 and 2027, and the same here. Nothing is committed, and the version stays 5.3.0.0.
  1. "Some parameters could not be created and were skipped: Projektnummer, Projektunternummer, Materialnummer" was a false alarm. They're in
     both sheets; the check didn't see what the same run had just created, so it made them a second time, which Revit refuses.
     Now each name is made once (`SharedParameterNames`), and only what the file lacks afterwards is warned of.
  2. Bauteil_ID wasn't put into the shared parameter file any more (since 5.2.2.0): the column only tells the rows apart. It's back, as a
     text; it's still never added to families and never written.
  3. "No matching elements found" on the second run: the workbook was read once per Revit session, so a value changed in Excel wasn't
     seen, and that text came whenever there was nothing to do. Now a click reads it again when it was saved since, also while it's
     open in Excel (`WorkbookFile`; Materials too), and a run with nothing to change says so (`ParameterRun`). It also names what couldn't
     be added or written.
  4. Found on the way: a project parameter was taken, with its values, off the categories the run didn't have, as ReInsert
     replaces them (Revit's API notes). Walls lost theirs to floors, or to the materials. Now categories are only added
     (`ProjectParameterBinder`). A type parameter of the project stays one and is named; it used to become an instance parameter.
  - 800 tests pass, 74 of them new; the Revit-free parts were broken on purpose, one by one, and a test failed each time. Outside Revit the
    reader gave Bauteil_ID from a copy of the real workbook, and the loader read a changed copy again, also while it was open as Excel has
    it. Putting parameters on categories needs Revit. All three years are compiled in both folders. A C# review, reading the code
    only, found nothing serious; taken up: what can't be written is named, and the parameters of a workbook that the materials
    button has read again reach the file too.
  - Not tried in Revit. Deployed (Debug, with step 11) to both 2027 add-in folders on Yehia's PC ("Yes install"), Revit closed; 2024 and 2026 there
    are the released 5.3.0.0. Left as it is: the definitions of an old file's group "Material" aren't used for elements, so
    Materialnummer isn't added with a file that has it only there.
  - Walk (Revit 2027):
    1. A new shared parameter file (Manage > Shared Parameters > Create…) and the Planungsdatenbank: no warning. Its group
       Kategorisierung has Bauteil_ID, and Typ is in the file once.
    2. Bauteil_ID as a project parameter on Generic Models, BT-1101-01 on an element: Add Parameters fills it. Once more: "1 element
       matches, and nothing was changed".
    3. With the workbook open in Excel, change a value and save: the next click says that it was read again, and Add Parameters writes it.
    4. Bauteil_ID on walls and floors too, two different IDs: Use Selection with the wall's, then with the floor's. The wall keeps its
       parameters and values. Then Populate Material Parameters: it still keeps them, and the materials have theirs.
- **2026-11-25, step 11 and the four fixes walked and committed:** Yehia: "I did the walkthrough for both, commit and push and we will release
  5.3.1.0 now, ofcourse without the database link, when colleagues run more extensive tests I will report back here". Committed and pushed
  with this entry: the fixes on `release-5.3.0`, where 5.3.1.0 is released from, and the fixes and step 11 on `database-link`. The database
  session can commit its migration of step 11, and step 12's rows may go into dev once Yehia says so. The 35 tests that need LocalDB still
  have to run before the merge on master. A merge of `release-5.3.0` has one conflict, in 2027's `SeedProjectElementsParameters.cs`, where
  `database-link`'s own version is the right one.
- **2026-11-25, the Excel import fills Bauteilnummer and Bauteilunternummer** (Yehia, before releasing 5.3.1.0: "I noticed the tool doesnt fcreate
  bauteilknummer and bauteilunternummer and so they dont get written into the obejcts", then "Yes, fill them"). Both were left out since the two
  of them were the key; since 2026-11-12 that's the Bauteil_ID. In both folders, all three years. Walked by Yehia the same day ("It works")
  and committed with this entry, on both branches, as part of what's released as 5.3.1.0.
  - For the Planungsdatenbank only, they're parameters like any other now: whole numbers, made in the shared parameter file, added, and written.
    The old Material-Bauteilliste, whose key Bauteilnummer is, and Bauteil_ID stay as they are. An element's own Bauteilnummer gets the Excel's
    number, also as a text.
  - A value of another kind than the model's parameter is skipped and named; Revit's notes don't say whether it would have been ignored or
    have stopped the whole run. A project's own parameter goes before Revit's of the same name ("Dicke", "Typ" in German).
  - Walls, floors and the like get a parameter only where they lack it, so a model that has Bauteilnummer from another shared parameter file
    doesn't get a second one.
  - Compare with Excel compares both. With the real workbook (a copy) and dev's kept pull, BT-1101-01's are the same, and "what identifies an
    object" has the Bauteil_ID only.
  - 804 tests pass; the new parts were broken on purpose, and a test failed each time. All three years are compiled in both folders. A second C#
    review, reading the code only, found no blocker; taken up: the project's own parameter first, and blank cells of the old workbook
    aren't named as not written. Not tried in Revit. Deployed (Debug) to both 2027 add-in folders on Yehia's PC ("Yes, install it"), Revit closed.
  - Walk: BT-1101-01 on a Generic Model without the two numbers: Add Parameters adds and fills them (1101 and 1). On one that has another
    Bauteilnummer: it gets 1101. A new shared parameter file has both, as whole numbers.
