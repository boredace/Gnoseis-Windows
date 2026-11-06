# Gnoseis for Windows

Gnoseis is a CRM application that can also be used as an all-purpose knowledge manager.
It stores all data on the device. This is the Windows (WinUI 3) port of
[Gnoseis for Android](../../Android/Gnoseis-Android), ported from the `tests` branch at commit `738b656`.

The Windows app uses **the same SQLite database format** as the Android app. A `gnoseis_data` file
or backup from Android can be used on Windows as long as it's schema version 3 or earlier (Android
0.3.0 writes version 3), and a Windows database or backup can be used on Android 0.3.0 or later. See [Supported schema versions](#supported-schema-versions).

## Features

The Windows app does everything the Android app does:

- Notes, contacts, organizations, categories and items: list, view, add, edit and delete.
- Link any record to records of other types, either by picking existing records or by creating a
  new linked record from a record's page.
- All records linked to a record shown on its page, by type with counts, and unlinked with Undo.
- Search across all record types, with ranked results, type filters and a best match, and create a
  record from a search that finds no record with that name.
- Back up the database to a file and restore it (Android: Settings › Import / Export).

## Solution layout

| Project | What it contains |
|---|---|
| `src/Gnoseis.Core` | .NET 10 class library: models, the Room-compatible SQLite data layer, repositories, backup/restore and validation. It has no UI code. |
| `src/Gnoseis.App` | The WinUI 3 desktop app (.NET 10, Windows App SDK 2.5, unpackaged, self-contained `.exe`). |
| `tests/Gnoseis.Core.Tests` | xUnit tests for the data layer, ported from the Android `tests` branch, plus database compatibility tests against the Room schema export. |
| `tools/Gnoseis.DemoData` | Console tool that creates a demo database (see [Demo database](#demo-database)). |

## Building and running

Requirements:

- Windows 10 version 1809 (build 17763) or later.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Visual Studio 2022 or later with the **Windows application development** workload (WinUI).

With Visual Studio, open `Gnoseis.sln`, select the `x64` platform and run **Gnoseis.App**
("Gnoseis (Unpackaged)" profile).

From the command line, build with Visual Studio's MSBuild (from a Developer PowerShell). `dotnet build`
can't build the WinUI project: it can't load Visual Studio's PRI packaging task.

```bash
msbuild Gnoseis.sln /restore /p:Configuration=Debug /p:Platform=x64
```

The tests need only the .NET SDK:

```bash
dotnet test tests/Gnoseis.Core.Tests
```

The app is then at `src\Gnoseis.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Gnoseis.exe`.
If it ever fails to start, the error is written to `%LOCALAPPDATA%\Gnoseis\crash.log`.

## Where the data is stored

```
%LOCALAPPDATA%\Gnoseis\gnoseis_data      the database (same file name as on Android)
%LOCALAPPDATA%\Gnoseis\settings.json     preferences, window position, recently opened records
```

To keep the data somewhere else (for example for testing), set the environment variable
`GNOSEIS_DATA_FOLDER` to a folder before starting the app.

**Settings › Database file › Open folder** opens this folder.

### Demo database

`tools/Gnoseis.DemoData` creates a demo database: the knowledge base of an independent IT consultant
with 10,000 records of each type, linked to each other (about 117,000 links). It contains:

- **Organizations**: clients, former clients, prospects, vendors and partners in 8 countries.
- **Contacts**: the people at these organizations, with job titles, phone numbers and e-mail addresses.
- **Categories**: a tag taxonomy (topics, status, sector and product tags) and client projects (`PRJ-2025-0412 …`).
- **Items**: servers, laptops, network devices, licences, service contracts, domains and certificates.
- **Notes**: five years of calls, tickets, meetings, maintenance logs, project updates, quotes,
  invoices, how-tos and quick notes.

All names are made up. The same `--seed` always gives the same data.

```bash
dotnet run --project tools/Gnoseis.DemoData -- --force
```

It writes `%LOCALAPPDATA%\Gnoseis-Demo\gnoseis_data`. It never writes into the real data folder.
Options: a different folder as the first argument, `--count N`, `--seed N`, and `--force` to replace
an existing demo file. To open the demo database, set `GNOSEIS_DATA_FOLDER` to
`%LOCALAPPDATA%\Gnoseis-Demo` before starting the app. The file can also be copied to Android.

### Using your Android database on Windows

Either:

1. On Android, open **Settings › Import / Export** and select **Export**. Copy the exported file to the
   PC. In Gnoseis for Windows, open **Settings › Restore database** and select the file. The file is
   checked before anything is replaced, and your current Windows data is kept as
   `gnoseis_data.before-restore-<date>.backup` in the database folder.

   **Or:**

2. Close Gnoseis for Windows, then copy the Android `gnoseis_data` file into `%LOCALAPPDATA%\Gnoseis\`.
   If the Android database folder also contains `gnoseis_data-wal`, copy it too, because it may hold
   the latest changes.

To go the other way, use **Settings › Back up database**. It writes a single self-contained file that
the Android app can import.

### Supported schema versions

The schema version is stored in the database file as `PRAGMA user_version`.

| Schema version | Written by | On Windows |
|---|---|---|
| 1 | Android before 0.2.4 | Opens. It's migrated to version 3 the first time it's opened or restored (see below). |
| 2 | Android 0.2.4 and 0.2.5 | Opens. It's migrated to version 3 the first time it's opened or restored (see below). |
| 3 | Android 0.3.0 and later, and this app | Opens as is. New databases and backups are version 3. |
| 4 or later | Not released yet | Doesn't open. The app explains that the file is from a newer version and leaves it unchanged. |

The migrations are the same as on Android:

- **1 to 2:** the tables don't change. Links that were stored more than once between the same two
  records are removed (the first one is kept), and indexes that Gnoseis doesn't use are dropped.
- **2 to 3:** every record gets the time it was created and last changed (`createdAt`, `updatedAt`),
  and every link the time it was made (`createdAt`), all in UTC epoch milliseconds. Existing records
  and links keep these empty, because their times aren't known. A new `Settings` table holds the
  time of the last backup (`lastBackupAt`) and of the last deletion of a record or link
  (`lastDeletionAt`). It starts empty.

Before migrating the database file, the app copies it to `gnoseis_data.before-v3-<date>.backup` in
the database folder. A restored version 1 or 2 backup is migrated on a temporary copy, so the backup
file itself isn't changed.

Android before 0.3.0 can't open a version 3 file, so update the Android app before copying a
Windows database or backup to it.

### Record times and settings

- A new record gets its created and updated time. Saving a record changes its updated time only if
  something was edited; saving without a change leaves the record as it was. A note's own date and
  time count as edits. The created time never changes.
- A new link gets its created time.
- **Back up database** saves the time of the backup in the backup file and, once the backup has
  succeeded, in the database. Deleting a record or removing a link saves the time of the deletion.
- A restore never changes these times: the restored database keeps what the backup holds.
- The Windows app doesn't show these times yet. It keeps them up to date, so the Android app shows
  the right backup reminder for a database that was used on Windows.

### Compatibility details

- The tables, columns, `PRAGMA user_version` (3) and Room's `room_master_table` identity hash
  (`d4a4ee67…`) are exactly the ones Room expects. The tests compare new and migrated databases
  against the Room schema export (`tests/Gnoseis.Core.Tests/RoomSchema/3.json`, copied from
  `Gnoseis-Android/app/schemas`).
- Ids are GUID strings and dates are epoch milliseconds, as on Android. Note dates are shown in UTC,
  like the Android app, so a note shows the same date on both platforms.
- Windows keeps the file in rollback-journal mode, so the main `gnoseis_data` file always holds all
  committed data. Room switches it back to WAL when Android opens it.
- A file that doesn't match (e.g. from a newer app version) is **never deleted or changed**. The
  app explains the problem and offers to start a new database, renaming the old file to
  `gnoseis_data.unusable-<date>`. The only file the app migrates is a version 1 file, after copying
  it as described above.

## Windows user experience

Gnoseis is meant to stay open all day, so the Windows version is built around quick capture, fast
switching and seeing related information at a glance.

- **Windows 11 title bar** with Back, the navigation pane toggle, and a search box that is always
  there (Ctrl+F). Search suggests the best matching records as you type; Enter opens the search
  page, from 3 characters. Results are ranked: an exact title first, then titles that start with
  the search, then a word that starts with it, then the rest, and the record with more links first
  within each. The top result is shown as the **best match**, with its links per type; the others
  are grouped by type, with the matched part highlighted and a line of context (a contact's job and
  company, a note's day, the link count). Type chips with counts narrow the results to one or more
  types. Before you type, the page shows the recently changed records. Enter in a section's filter
  box opens the search for that section's type. Back returns to the same search. A contact is
  found as "Last, First" and as "First Last". When nothing is found, or
  nothing has exactly the name searched for, the results offer to create a note, contact,
  organization, category or item with that name: it opens ready to edit, with the name filled in (a
  contact's words split into first and last name).
- **Home**: an overview of the whole database. Buttons at the top add a note, contact,
  organization, category or item. Count tiles show how many records of each type there are (each
  opens its list) and how many links. **Most linked** shows the records with the most links as
  bubbles in their type's colours, bigger for more links, with dashed lines between records linked
  to each other; a type picker shows one type at a time, and a bubble opens its record. A chart
  shows the notes written on each of the last 14 days, and **Unlinked notes** lists the newest notes
  not linked to anything yet, each with a **Link** button. **Recent notes** and **Recently opened**
  list records with what they hold and their link counts. A reminder at the bottom says when the
  database has never been backed up, or when records changed or were deleted since the last backup,
  with **Back up now**. On a narrow window the sections stack in one column.
- **List and record side by side** in every section, like Mail and Outlook. You can move through the
  list with the arrow keys. A filter box narrows the list. Notes are grouped by *Today*,
  *Yesterday*, weekday and date, and the other lists by first letter.
- **Record page** for notes, organizations, categories and items: a header with the type, the
  title and the commands, the record's details, and its linked records. The note text is readable,
  with clickable web and e-mail links.
- **Contact page**, built around the person: a header with the name, the job and company (the
  company opens its organization when it's linked, or offers **Link as organization**), and a chip
  per phone number and e-mail address that calls or writes (right-click to copy; the value is copied
  when no app handles it). Below it, a note box writes a note linked to the contact without leaving
  the page (Ctrl+Shift+N, Ctrl+Enter saves), and the contact's history lists its notes, newest
  first, with month headers, a filter, year chips and **Show older** for long histories. The
  organizations, categories and items sit in a side column, each with a **+** to link one more.
  The layout follows the width of the record pane: tabs on a narrow one, a side column from 860 px,
  and from 1700 px a reading pane that shows the selected note in full (Up and Down move through the
  notes). Once the header scrolls away, a compact header keeps the name, the year chips and the
  commands in view; Home returns to the top. **More** has **New linked**, **Copy contact details**
  (Ctrl+Shift+C) and **Delete**.
- **Linked records by type**: a selector shows **All**, then each linked type with its count, the
  type with the most links first. **All** shows the 3 latest records of each type, with *Show all*.
  Notes are grouped by day. Each row shows what the record holds (a note's first line, a contact's
  job, company and phone or e-mail, a category's or item's parent path) and how many records are
  linked to it. On a wide record pane the details and the linked records sit side by side, and the
  linked records flow into columns; on a narrower one they are tabs: **Details**, **All**, then the
  types.
- **Unlink** from a linked record's context menu (right-click, Shift+F10 or the menu key). There's
  no confirmation: a message offers **Undo** until you close it or leave the page.
- **Editing happens in place** in the record pane, on one edit page for every type, with Save
  (Ctrl+S) and Cancel (Esc). A record created with **New linked** shows which record it will be
  linked to. You're asked before unsaved changes are lost, including when you click another record.
- **Linking** existing records uses a picker dialog with the same search, chips and best match as
  the search page, and multi-select. Records stay ticked through later searches and chips until
  you untick them; a strip shows how many are ticked, and **Show selected** lists them all. Records
  that are already linked are dimmed. When the search finds no record with that name, the picker offers to
  create one, linked to the record it was opened from; records already ticked are linked too.
- **Delete** asks first (the safe answer is the default), then moves on to the next record.
- **Colours per record type**: the same colours as Gnoseis for Android, in light and dark theme, so
  record types can be told apart at a glance (also with night light on). A high-contrast theme uses
  the system colours instead.
- **Remembers where you were**: window size and position, whether it was maximized, the navigation
  pane and the last section, plus the selected record in each section while the app is open.
- **Narrow windows** show one pane at a time; Back returns from a record to the list.
- **Keyboard**: Ctrl+1 to Ctrl+6 switch sections, Ctrl+N creates a record, Ctrl+E edits, Ctrl+L
  links, Del deletes, Alt+Left or the mouse back button goes back. On a contact, Ctrl+Shift+N writes
  a note and Ctrl+Shift+C copies the contact details. Settings lists every shortcut.
- Fluent design with Mica, a light, dark or system theme, and screen-reader names and headings.

| Android | Windows |
|---|---|
| Navigation drawer | Navigation pane (expanded, compact or hidden, depending on window width) |
| Separate list and details screens | List and record side by side |
| Floating "Add" button | **New** button in the list header (Ctrl+N) |
| Expandable FAB (link existing / new linked record) | **Link** (Ctrl+L) and **New linked** menu in the record's command bar (on a contact, under **More**) |
| Contact details screen (fields, then linked records) | Contact page: phone and e-mail chips in the header, the notes as a history with a note box above it, the other linked records in a side column |
| New linked note from a contact | Note box on the contact page (or **Open full editor**) |
| Linked-record tab row | Selector with **All** and each linked type, beside the details on a wide pane, tabs with **Details** on a narrow one |
| Snackbar with Undo after unlinking | Info bar with **Undo** at the bottom of the record page |
| Link Records page | Link picker dialog |
| Search page | Search box in the title bar, with suggestions and a results page |
| Import / Export page | Settings page cards: theme, database location, back up, restore, shortcuts |


## Licensing

Gnoseis is dual-licensed:

1. **Open-Source License**: [GNU General Public License v3.0](LICENSE).
2. **Commercial License**: for proprietary, closed-source or commercial use, see
   [COMMERCIAL_LICENSE](COMMERCIAL_LICENSE).

By contributing to this project, you agree to license your contributions under both licenses.
