# Database link: MH database → Revit 2027

**Status (2026-11-19):** step 1 is done: Yehia walked it and Test connection connected in Revit 2027.
Step 2 starts when Yehia says go. Nothing committed.

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
  "trustServerCertificate": true
}
```

On Yehia's PC for development (LocalDB, Windows sign-in, no password):

```json
{
  "server": "(localdb)\\MSSQLLocalDB",
  "database": "MH_Database",
  "windowsSignIn": true
}
```

- `encrypt` and `trustServerCertificate` are whatever CADSQL needs, the same as Studio uses (asked for
  under "Needs from the database").
- This file doesn't hold anyone's own settings:
  - how often to check and auto-apply: `%APPDATA%\MH\RevitTools\settings.json`
  - the last pull: `%LOCALAPPDATA%\MH\RevitTools\`
- If DPAPI comes later, an encrypted `protectedPassword` replaces `password`.

## How the code is split

- **`MH.RevitTools.Database`** (new, no Revit). It holds:
  - the connection file
  - the `revit` queries and the change check
  - the cache of the last pull
  - the project-number rule
  - the Studio password hashing

  It targets net48 and net10.0, so 2024 and 2028 only need to add a reference. It uses only packages
  already in the repo (Microsoft.Data.SqlClient 5.2.2, Newtonsoft.Json 13.0.3). All database access
  goes through one interface, with the real SQL version and a fake.
- **`MH.RevitTools.Database.Tests`** (new): xUnit. The query tests create their own throwaway LocalDB
  database and drop it afterwards, never MH_Database.
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
- The SQL version of the interface is written against that contract and tested against a throwaway
  LocalDB database that mimics the views. Revit uses the fake until the real views exist.
- **Walk:** read "Needs from the database" and pass it on. Nothing to click in Revit.

### 3. Link the model to its project
- The dialog from the mockup, with the suggestion from the Project Number (rule above). The link is
  saved in the model (Extensible Storage on Project Information).
- **Walk:** open a test model whose Project Number is 1761-01 → Database → Link → "1761-01 Ulm" is
  suggested → Link. Save, close and reopen the model: it's still linked.

### 4. Check parameters
- Lists the database's parameters for the model's categories as Ready, Missing or Other GUID. "Add
  missing" binds them to the categories from the master shared parameter file. Families aren't reloaded
  and values aren't touched.
- Until Studio exports the real master file, a small test file that matches the fake stands in.
- **Walk:** Check parameters shows 3 missing → Add → they appear on a Generic Model with their plain
  names.

### 5. The pane and the change check (no apply yet)
- The docked "MH Database" pane, with these states: up to date, n changes, not reachable, not set up,
  not signed in.
- The check runs in the background every minute and reads only the change stamp. When the stamp moves,
  it reads the project's rows again. It never touches the model. The last pull is cached per user, and
  the pane shows when it was taken.
- **Walk:**
  1. Open the linked model: the pane says "Up to date · checked 14:32".
  2. Change a value in the fake: within a minute it shows "1 change from the database".
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
- It needs the real views with 1761's data in dev MH_Database, so it waits for the database session. It
  can move earlier as soon as they exist.
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
- **Walk:** run the package on a second PC → Test connection works there.

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
- **Check parameters, "Replace…" on Other GUID:** Revit can't change a parameter's GUID. Replacing
  means deleting the old parameter, and that deletes its values in every element. With fresh GUIDs and
  no existing models, I'd show "Other GUID" without a Replace button for now.
- **Who and when on each change** ("M. Keller · 14:28"): shown only if the database provides it (asked
  for below).
- **Docking:** the pane opens on the right the first time. After that Revit remembers where the user put
  it. Revit only allows a pane to be registered at startup, so the Database button shows and hides it.
- **Master shared parameter file:** Revit only opens the shared parameter file set in its options. The
  add-in switches to the master file for a moment, then puts the user's own setting back.
- **Fonts:** Segoe UI, which is what Revit uses, instead of Inter.

## Needs from the database

*Draft. It's final after step 2. Yehia passes it to the database session.*

**Role and login**
- A database role (name is your call, e.g. `revit_reader`) with SELECT on schema `revit`, EXECUTE on
  the two sign-in procedures below, and nothing else. In particular, no access to `dbo.Users`.
- One SQL login for the add-in, a member of that role. Please use CHECK_EXPIRATION OFF: if this
  password expired, the link would stop on every PC at once. The password goes only into the connection
  file.
- Tell me what Studio uses for Encrypt / TrustServerCertificate on CADSQL. SqlClient 5 encrypts by
  default.

**Views in schema `revit`**

| View | One row is | Columns |
|---|---|---|
| `revit.Projects` | one project | ProjectId int (stable; saved in the model), ProjectNumber int, ProjectSubNumber int, Name nvarchar |
| `revit.Parameters` | one dictionary parameter that can go to Revit | ParameterId int, Guid uniqueidentifier (the master file's GUID), RevitName nvarchar (settled, with suffix), SetCode, GroupCode, Name (the lookup key, e.g. BAU / GEO / Dicke), DataType nvarchar (String/Integer/Double/Boolean/Date/Url/Coordinates), Unit nvarchar (spelled like the Excel's Einheit: m, cm, mm, m2, m3, grad, or empty), Target (Object or Material) |
| `revit.ParameterCategories` | one parameter on one Revit category | ParameterId int, Category nvarchar as Revit's BuiltInCategory name (OST_GenericModel, OST_Materials…), so it doesn't depend on Revit's language. If the dictionary only knows "element"/"material", tell me and we'll agree on a fixed list. |
| `revit.ObjectValues` | one value of one parameter on one object (Bauteil) in one project | ProjectId int, BauteilId nvarchar (e.g. BT-1101-01), ParameterId int, Value nvarchar, ChangedAt datetime2 (optional), ChangedBy nvarchar display name (optional) |
| `revit.Materials` | one material in one project | ProjectId int, MaterialId nvarchar (e.g. MB-1761-01-BET-0011), MaterialName, MaterialGroup, SurfacePatternId, CutPatternId |
| `revit.MaterialValues` | one value of one parameter on one material | ProjectId int, MaterialId nvarchar, ParameterId int, Value nvarchar, ChangedAt, ChangedBy (optional) |
| `revit.ProjectStamps` | one project | ProjectId int, Stamp binary(8) or bigint |

- **Values:**
  - Numbers use the invariant format with a dot ("0.3"), no thousands separator, in the parameter's Unit.
  - Placeholders ([Modeller], [ET], -, TBT) come through as stored. The add-in skips them and lists
    them under "Not applied".
  - Tell me how booleans and dates are written, so they match the Excel.
- **Objects without a Bauteil_ID** can't match anything in Revit. Leave them out of `ObjectValues`.
- **The stamp** has to change whenever anything the add-in reads for that project changes. That
  includes deletions and dictionary changes (names, GUIDs, units, categories). A MAX over rowversions
  alone misses deletions. The add-in reads only this view every minute.

**Sign-in procedures (Studio accounts; the stored hash never leaves the server)**
- `revit.GetSignInSalt(@Username)` returns the user's salt, base64 as stored. For an unknown or
  inactive user it returns a made-up but stable salt, so the answer doesn't give away who exists.
- `revit.CheckSignIn(@Username, @Hash)` returns one row (IsValid bit, DisplayName, Role) for an active
  user whose hash matches.
- The add-in computes `@Hash` exactly like Studio's `PasswordHashService`: PBKDF2, SHA-512, 100,000
  rounds, 32-byte salt, base64. If Studio ever changes this, Revit's sign-in has to change in the same
  release. Please note that in REVIT_LINK.md.
- Both procedures run with their owner's rights, so the role needs no rights on `dbo.Users`.

**Master shared parameter file**
- Its UNC path on the share.
- Studio should write it in one go (to a temporary file, then rename), so the add-in never reads half a
  file.

## Questions

Answered by Yehia on 2026-11-19 (recorded above):
1. Sign-in: use the two procedures instead of reading `Users`, and put the file in ProgramData. Yes.
2. Encrypt the password in the file (DPAPI): maybe later. Plain text for now.
3. Project Number: `1761` without a sub-number, `1761-01` with one.
4. Who may sign in: every active Studio account.

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
