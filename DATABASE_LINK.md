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
their own, and deletes them only after he confirms. Yehia tried it: "everything is good". Committed and
pushed with this note.

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
    build output or on the share. Its format is under "The connection file" below.
  - Every active Studio account may sign in. There's no role check.
  - Encrypting the password for the PC (Windows DPAPI, machine scope) may come later. For now the file
    holds it as plain text, so anyone who can open the file on that PC can read it.
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
- **Parameter definitions come from Studio's master file** (Yehia, 2026-11-20, step 4), as in the
  mockup. The connection file names the file, so each PC pairs a database with its own file, and the
  add-in refuses a file that holds none of the database's GUIDs.

## The connection file

`C:\ProgramData\MH\RevitTools\database.json`, one per PC, written by the installer (step 9). It's never
in the repo, the build output or on the share. Everyone on the PC can read it; the add-in only reads it.

In the office. The values in angle brackets come from the database session's login script and from the
installer; no real password is ever written here:

```json
{
  "server": "CADSQL.mh-frlg.local,1433",
  "database": "MH_Database",
  "login": "<read-only login>",
  "password": "<written by the installer>",
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

- `encrypt` and `trustServerCertificate` are whatever CADSQL needs, the same as Studio uses (asked for
  under "Needs from the database"). `"trustServerCertificate": true` means the server's certificate
  isn't checked, which puts the shared password at risk on the network. A certificate the PCs trust
  avoids that.
- This file doesn't hold anyone's own settings:
  - how often to check and auto-apply: `%APPDATA%\MH\RevitTools\settings.json`
  - the last pull: `%LOCALAPPDATA%\MH\RevitTools\`
- If DPAPI comes later, an encrypted `protectedPassword` replaces `password`.

## How the code is split

- **`MH.RevitTools.Database`** (new, no Revit). It holds:
  - the connection file
  - the reads of the `revit` views and the change check
  - the cache of the last pull
  - the project-number rule and what the Link dialog says (`Linking`)
  - the Studio password hashing

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
- **Reused, not rewritten:** `ElementKey` (matching by Bauteil_ID), `PlanungsdatenbankLayout.IsSkipped`
  (placeholders), `ParamMetaData.ConvertValue` (invariant parsing and unit conversion, already used for
  the Excel's Einheit).
- **One refactor.** Today the value-writing loop sits inside `SeedProjectElementsParameters.AddParametersToElements`,
  which also adds parameters to families. The link must never do that, so the loop moves into its own
  method that Excel and database both call. The Excel path behaves exactly as before.
- **One change to that loop.** With the database, a parameter is found by its GUID. The Excel path
  keeps finding parameters by name. It also reports what it skipped instead of skipping silently, which
  is what "Not applied" needs.

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
    copies only the top-level `*.dll`. Old `runtimes\` folders stay in installed add-in folders, because
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
- The big **Database** button opens the dialog from the mockup (until 2026-11-22 it was a Link project
  button; from step 5 the pane offers it). It reads the projects in the
  background and suggests one from the Project Number (rule above).
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
  project's number (1761-01) and a green dot on the icon once the model is linked; "Database" over "Link
  model" while it isn't; plain "Database" without a project model. The tooltip names the project, who
  linked it and when. The button follows the active view from model to model, Link project, Undo and
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
- The docked "MH Database" pane, with these states: up to date, n changes, not reachable, not set up,
  not signed in.
- The check runs in the background every minute and reads only the change stamp. When the stamp moves,
  it reads the project's rows again. It never touches the model. The last pull is cached per user, and
  the pane shows when it was taken.
- The first check in each Revit session reads everything, whatever the stamp says: a Studio migration
  can change data without moving the stamp.
- **Walk:**
  1. Open the linked model: the pane says "Up to date · checked 14:32".
  2. Change a value of project 1761 in Studio: within a minute it shows "1 change from the database",
     with your name and the time.
  3. Rename the connection file: it shows "Not reachable · last answer 14:32".

  Revit stays responsive the whole time.

### 6. Apply and "Not applied"
- Apply all, or apply per object, through an ExternalEvent, with one undo step each.
- "Not applied" lists:
  - placeholders ([Modeller], [ET], -, TBT)
  - elements someone else has borrowed
  - missing or read-only parameters
- **Walk** (needs a test model with a few Bauteil_IDs from 1761): Apply all → the values change →
  Ctrl+Z undoes it. A borrowed element shows under "Not applied".

### 7. Excel and database give the same result (the acceptance)
- A temporary **Compare with Excel** button. It loads Planungsdatenbank.xlsx and project 1761 from the
  database and lists every difference: objects, parameters, values after unit conversion, and materials.
- It reads 1761 through the views, so it can move earlier as soon as they exist in dev MH_Database.
- **Walk:** Compare → pick Planungsdatenbank.xlsx → "Identical", or the list of differences.

### 8. Sign in with your Studio account
- Revit asks once per start, the first time the pane needs the database. Nothing is saved. A wrong
  password or an inactive account means no link.
- It needs the two sign-in procedures.
- **Walk:**
  1. Open Revit: the pane asks you to sign in.
  2. A wrong password is refused; the right one works.
  3. Restart Revit: it asks again.

### 9. Installer
- `Install.ps1` writes `C:\ProgramData\MH\RevitTools\database.json` from a file Yehia keeps next to the
  package. That file is never in the repo or on the share.
- It also locks the folder and the file: Administrators and SYSTEM full control, Users read only,
  inheritance off. By default any user may create files under `C:\ProgramData`. Without the lock, anyone
  could put a file there before the installer runs, or own the folder, and point Revit at another
  server. The add-in folder `Addins\2027\MH.RevitTools` has the same default (users can add files), so
  lock it the same way.
- **Walk:** run the package on a second PC → Test connection (in the Database panel's slide-out) works
  there.

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
- **The Database panel** follows the mockup since 2026-11-22: the big Database button, then three rows
  (Apply changes, Check parameters, Sign in). Until the pane comes (step 5), Database opens the Link
  dialog. Apply changes (step 6) and Sign in (step 8) stay greyed out until they're built. Test
  connection sits in the panel's slide-out (the arrow under the panel). The small icons are a lighter
  grey than the mockup's, so they also show on Revit's dark ribbon.
- **The Database button's second line** is the pane's state in the mockup ("3 changes", "Up to date",
  "Link model"). Until the pane comes, a linked model shows its project number there, and the icon gets
  a green dot in the place of the mockup's status dots (Yehia, 2026-11-22). The dot sits inside the
  32 px icon, as Revit's icons can't reach past their edge.
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

## Needs from the database

*Final version, 2026-11-19. Items 1 and 2 are built and the questions are answered (see "What the
database session built"). Items 3 and 4 come later.*

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
3. **Later, step 8:** the two sign-in procedures (below).
4. **Later, step 9:** the read-only login (below).

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
  object's values include those references.

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

**The read-only login (step 9, for the office)**
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
  goes only into the connection file.
- The login is shared, so whoever holds the connection file can read every project, through the views
  only. That's accepted.
- Tell me what Studio uses for Encrypt and TrustServerCertificate on CADSQL. If the PCs don't trust
  CADSQL's certificate, the file needs `"trustServerCertificate": true`. Then the certificate isn't
  checked, and someone on the office network could pose as the server and catch the shared password.
  Better: give CADSQL a certificate the PCs trust (an internal CA is enough), so the file can say false.

**If AuditLogs grows large:** the views find the newest row per value through the existing index
IX_AuditLogs_EntityName_EntityKey. The stamp counts the whole history every minute, which is quick at
today's size. Tell me if the history gets trimmed or grows very large.

**Sign-in procedures (step 8; Studio accounts; the stored hash never leaves the server).** Put them in
schema `revit`, so the schema grant covers them.
- `revit.GetSignInSalt(@Username)` returns the user's salt, base64 as stored. For an unknown or inactive
  user it returns a made-up but stable salt, so the answer doesn't give away who exists. Make that salt
  an HMAC of the name with a secret kept on the server; a plain hash of the name can be checked by
  anyone. Keep the secret where the `revit` role can't read it, e.g. in a dbo table that only the
  procedure reads, under its owner's rights.
- `revit.CheckSignIn(@Username, @Hash)` returns one row (IsValid bit, DisplayName, Role) for an active
  user whose hash matches.
- `CheckSignIn` slows down after failed attempts: an increasing delay per user, and at most a short lock
  that lifts on its own. Never a permanent lock: the login is shared, so anyone holding the connection
  file could otherwise lock every Studio user out, TheAdmin included. Log the attempts. Without any
  limit, a password could be guessed at leisure from any PC.
- That attempt count is the only thing in the link that writes, and it does so under the procedure
  owner's rights. Everything else only reads.
- No dynamic SQL in either procedure.
- The add-in computes `@Hash` exactly like Studio's `PasswordHashService`: PBKDF2, SHA-512, 100,000
  rounds, 32-byte salt, base64. If Studio ever changes this, Revit's sign-in has to change in the same
  release. Please note that in REVIT_LINK.md.
- Both procedures run with their owner's rights, so the role needs no rights on `dbo.Users`.

## Questions

Answered by Yehia on 2026-11-19 (recorded above):
1. Sign-in: use the two procedures instead of reading `Users`, and put the file in ProgramData. Yes.
2. Encrypt the password in the file (DPAPI): maybe later. Plain text for now.
3. Project Number: `1761` without a sub-number, `1761-01` with one.
4. Who may sign in: every active Studio account.

Answered by Yehia on 2026-11-20:
5. Where the parameter definitions come from (step 4): Studio's master file, named in the connection
   file, as in the mockup.

Still open:
- The read-only login's name. It comes with the database session's login script.

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
  - Yehia tried it: "everything is good". Committed and pushed with this entry.
  - He also asked whether the pane's changes come from Studio's history and activity, and so are the same
    across the database: yes. The stamp and each value's who/when come from AuditLogs in MH_Database,
    which every PC reads. The add-in never writes to the database, so what Revit applies doesn't appear
    in Studio's history.
