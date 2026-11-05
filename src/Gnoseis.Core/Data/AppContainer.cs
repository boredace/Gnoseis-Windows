/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.Core.Data;

/// <summary>Holds the database and the repositories used by the app (the Android AppContainer).</summary>
public sealed class AppContainer
{
    public AppContainer(GnoseisDatabase database)
    {
        Database = database;
        Notes = new(database);
        Contacts = new(database);
        Organizations = new(database);
        Categories = new(database);
        Items = new(database);
        LinkedRecords = new(database);
        Search = new(database);
        Backup = new(database);
        Settings = new(database);
        DeleteRecord = new(database);
        UnlinkRecords = new(database, LinkedRecords);
        GetLinkSource = new(Notes, Contacts, Organizations, Categories, Items);
    }

    public GnoseisDatabase Database { get; }
    public NoteRepository Notes { get; }
    public ContactRepository Contacts { get; }
    public OrganizationRepository Organizations { get; }
    public CategoryRepository Categories { get; }
    public ItemRepository Items { get; }
    public LinkedRecordRepository LinkedRecords { get; }
    public SearchRepository Search { get; }
    public BackupRepository Backup { get; }
    public SettingsRepository Settings { get; }
    public DeleteRecordUseCase DeleteRecord { get; }
    public UnlinkRecordsUseCase UnlinkRecords { get; }
    public GetLinkSourceUseCase GetLinkSource { get; }
}
