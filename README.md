# Gnoseis for Windows

Gnoseis is a CRM application that can also be used as an all-purpose knowledge manager.
It stores all data on the device. This is the Windows (WinUI 3) port of
[Gnoseis for Android](../../Android/Gnoseis-Android), ported from the `tests` branch at commit `738b656`.

The Windows app uses **the same SQLite database format** as the Android app. A `gnoseis_data` file
from Android can be used on Windows as is, and a Windows database or backup can be used on Android.

## Features

The Windows app does everything the Android app does:

- Notes, contacts, organizations, categories and items: list, view, add, edit and delete.
- Link any record to records of other types, either by picking existing records or by creating a
  new linked record from a record's page.
- All records linked to a record shown on its page, grouped by type.
- Search across all record types.
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

### Compatibility details

- The tables, columns, `PRAGMA user_version` (1) and Room's `room_master_table` identity hash
  (`e7731af7…`) are exactly the ones Room expects. The tests compare the database against the Room
  schema export (`tests/Gnoseis.Core.Tests/RoomSchema/1.json`, copied from
  `Gnoseis-Android/app/schemas`).
- Ids are GUID strings and dates are epoch milliseconds, as on Android. Note dates are shown in UTC,
  like the Android app, so a note shows the same date on both platforms.
- Windows keeps the file in rollback-journal mode, so the main `gnoseis_data` file always holds all
  committed data. Room switches it back to WAL when Android opens it.
- A file that doesn't match (e.g. from a newer app version) is **never deleted or migrated**. The
  app explains the problem and offers to start a new database, renaming the old file to
  `gnoseis_data.unusable-<date>`.

## Windows user experience

Gnoseis is meant to stay open all day, so the Windows version is built around quick capture, fast
switching and seeing related information at a glance.

- **Windows 11 title bar** with Back, the navigation pane toggle, and a search box that is always
  there (Ctrl+F). Search suggests matching records as you type; Enter shows all results.
- **Home**: a quick-note box (Ctrl+Enter adds the note), the number of notes, contacts, organizations,
  categories and items, recent notes, and the records you opened recently.
- **List and record side by side** in every section, like Mail and Outlook. You can move through the
  list with the arrow keys. A filter box narrows the list. Notes are grouped by *Today*,
  *Yesterday*, weekday and date, and the other lists by first letter.
- **Record page**: the note text is readable, with clickable web and e-mail links. A contact shows
  initials and field cards, and phone numbers and e-mail addresses open the calling or mail app.
  **All linked records are visible at once**, grouped by type with counts. Right-click a linked
  record to remove the link.
- **Editing happens in place** in the record pane, with Save (Ctrl+S) and Cancel (Esc). You're asked
  before unsaved changes are lost, including when you click another record.
- **Linking** existing records uses a picker dialog with search and multi-select. Records that are
  already linked are dimmed.
- **Delete** asks first (the safe answer is the default), then moves on to the next record.
- **Colours per record type** (notes amber, contacts blue, organizations purple, categories green,
  items teal), so record types can be told apart at a glance.
- **Remembers where you were**: window size and position, whether it was maximized, the navigation
  pane and the last section, plus the selected record in each section while the app is open.
- **Narrow windows** show one pane at a time; Back returns from a record to the list.
- **Keyboard**: Ctrl+1 to Ctrl+6 switch sections, Ctrl+N creates a record, Ctrl+E edits, Ctrl+L
  links, Del deletes, Alt+Left or the mouse back button goes back. Settings lists every shortcut.
- Fluent design with Mica, a light, dark or system theme, and screen-reader names and headings.

| Android | Windows |
|---|---|
| Navigation drawer | Navigation pane (expanded, compact or hidden, depending on window width) |
| Separate list and details screens | List and record side by side |
| Floating "Add" button | **New** button in the list header (Ctrl+N) |
| Expandable FAB (link existing / new linked record) | **Link** (Ctrl+L) and **New linked** menu in the record's command bar |
| Linked-record tab row | Linked records panel showing every type at once |
| Link Records page | Link picker dialog |
| Search page | Search box in the title bar, with suggestions and a results page |
| Import / Export page | Settings page cards: theme, database location, back up, restore, shortcuts |


## Licensing

Gnoseis is dual-licensed:

1. **Open-Source License**: [GNU General Public License v3.0](LICENSE).
2. **Commercial License**: for proprietary, closed-source or commercial use, see
   [COMMERCIAL_LICENSE](COMMERCIAL_LICENSE).

By contributing to this project, you agree to license your contributions under both licenses.
