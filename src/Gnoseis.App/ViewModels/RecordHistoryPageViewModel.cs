/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>
/// One item or organization: its name, comments, history (the linked notes) and the other
/// records linked to it; for an organization also its people (the linked contacts, with the notes
/// linked to both) and the contacts whose company is its name but who aren't linked yet.
/// </summary>
public sealed class RecordHistoryPageViewModel(RecordRef record) : PageViewModel
{
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;

    public RecordType Type => Record.Type;

    public bool IsOrganization => Type == RecordType.Organization;

    public Item? Item { get; private set; }

    public Organization? Organization { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsMissing { get; private set; }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public LinkedRecordSet Linked { get; private set; } = LinkedRecordSet.Empty;

    /// <summary>The linked notes, newest first.</summary>
    public IReadOnlyList<HistoryNote> History { get; private set; } = [];

    /// <summary>When the record was created; null before schema v3.</summary>
    public long? CreatedAt { get; private set; }

    /// <summary>An organization's linked contacts, in the People column's order; empty for an item.</summary>
    public IReadOnlyList<PersonSummary> People { get; private set; } = [];

    /// <summary>Contacts whose company is the organization's name but who aren't linked: the first 3, and how many in all.</summary>
    public (IReadOnlyList<NotLinkedContact> Shown, int TotalCount) NotLinked { get; private set; } = ([], 0);

    private string StoredName => (IsOrganization ? Organization?.OrganizationName : Item?.ItemName)?.Trim() ?? "";

    /// <summary>The name, or the type's name when blank.</summary>
    public string Name => StoredName.Length > 0 ? StoredName : Type.DisplayName();

    /// <summary>The header line: "Item · 26 notes · 16 linked", "Organization · 7 people · 64 notes · 21 other linked", or a placeholder.</summary>
    public (string Text, bool IsPlaceholder) HeaderLine
    {
        get
        {
            var notes = Linked.Find(RecordType.Note)?.Count ?? 0;
            if (!IsOrganization)
            {
                return ItemPage.HeaderLine(notes, Linked.TotalCount - notes);
            }
            var people = Linked.Find(RecordType.Contact)?.Count ?? 0;
            return OrganizationPage.HeaderLine(people, notes, Linked.TotalCount - notes - people);
        }
    }

    /// <summary>The comments, as typed; empty when there are none.</summary>
    public string Comments => ((IsOrganization ? Organization?.Comments : Item?.Comments) ?? "").Trim();

    public bool HasComments => Comments.Length > 0;

    /// <summary>The side column's types: an item's contacts, organizations, categories and items; an organization's categories, items and organizations.</summary>
    public IReadOnlyList<RecordType> SideTypes => IsOrganization ? OrganizationPage.SideTypes : ItemPage.SideTypes;

    /// <summary>A side column section's records: latest linked first.</summary>
    public IReadOnlyList<LinkedRecordSummary> SideRecords(RecordType type) => RecordPageLayout.SideOrder(Linked.Find(type)?.Records ?? []);

    /// <summary>Records in the side column's sections (people not counted).</summary>
    public int SideCount => SideTypes.Sum(t => Linked.Find(t)?.Count ?? 0);

    /// <summary>Text for the copy command.</summary>
    public string CopyText => IsOrganization
        ? Organization is { } org ? OrganizationPage.CopyText(org) : ""
        : Item is { } item ? ItemPage.CopyText(item) : "";

    /// <summary>Raised after each load, so the page can rebuild.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            Item? item = null;
            Organization? organization = null;
            if (IsOrganization)
            {
                organization = await Data.Organizations.GetOrganizationAsync(Record.Id);
            }
            else
            {
                item = await Data.Items.GetItemAsync(Record.Id);
            }
            var exists = item != null || organization != null;

            var linked = exists ? Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : Task.FromResult(LinkedRecordSet.Empty);
            var history = exists ? Data.LinkedRecords.GetLinkedNotesWithLinksAsync(Record.Id) : Task.FromResult(new List<HistoryNote>());
            var createdAt = !exists ? Task.FromResult<long?>(null)
                : IsOrganization ? Data.Organizations.GetCreatedAtAsync(Record.Id)
                : Data.Items.GetCreatedAtAsync(Record.Id);
            var contacts = organization != null ? Data.LinkedRecords.GetLinkedContactsAsync(Record.Id) : Task.FromResult(new List<Contact>());
            var candidates = organization != null ? Data.Contacts.GetContactsByCompanyAsync(organization.OrganizationName) : Task.FromResult(new List<Contact>());
            await Task.WhenAll(linked, history, createdAt, contacts, candidates);
            if (version != _loadVersion)
            {
                return;
            }

            Item = item;
            Organization = organization;
            IsMissing = !exists;
            Linked = linked.Result;
            History = ContactHistory.Order(history.Result);
            CreatedAt = createdAt.Result;
            People = organization != null ? OrganizationPage.People(contacts.Result, History) : [];
            NotLinked = organization != null
                ? OrganizationPage.NotLinkedContacts(organization, contacts.Result.Select(c => c.Id), candidates.Result)
                : ([], 0);
            IsLoaded = true;
            if (exists && !_addedToRecents)
            {
                _addedToRecents = true;
                App.Settings.AddRecent(Type, Record.Id);
            }
            Loaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = LoadAsync();

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, Type);

    /// <summary>Unlinks a record. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(string recordId) => Data.UnlinkRecords.InvokeAsync(Record.Id, recordId);

    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Links a contact to the organization (does nothing when they are linked already).</summary>
    public Task LinkContactAsync(string contactId) =>
        Data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
        {
            OwnerDbId = "db1",
            Record1Id = Record.Id,
            Record2Id = contactId,
            Record1TypeId = (int)Type,
            Record2TypeId = (int)RecordType.Contact,
        });

    /// <summary>Text of the delete confirmation (same wording as the other record pages).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var type = Type.DisplayName().ToLowerInvariant();
            var text = $"This {type} will be permanently deleted.";
            var count = Linked.TotalCount;
            if (count == 1)
            {
                text += $"\n\nThe link to 1 record linked to this {type} will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this {type} will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }
}
