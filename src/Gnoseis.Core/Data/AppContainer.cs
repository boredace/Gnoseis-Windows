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
public sealed class AppContainer(GnoseisDatabase database)
{
    public GnoseisDatabase Database { get; } = database;
    public NoteRepository Notes { get; } = new(database);
    public ContactRepository Contacts { get; } = new(database);
    public OrganizationRepository Organizations { get; } = new(database);
    public CategoryRepository Categories { get; } = new(database);
    public ItemRepository Items { get; } = new(database);
    public LinkedRecordRepository LinkedRecords { get; } = new(database);
    public SearchRepository Search { get; } = new(database);
    public BackupRepository Backup { get; } = new(database);
    public DeleteRecordUseCase DeleteRecord { get; } = new(database);
}
