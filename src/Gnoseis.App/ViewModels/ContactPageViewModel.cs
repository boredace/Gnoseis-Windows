/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>A note's row in the contact's history.</summary>
public sealed class HistoryRow(HistoryNote note, string contactId)
{
    public HistoryNote Note { get; } = note;
    public string Id => Note.Id;
    public string DayText { get; } = ContactHistory.DayText(note);
    public string WeekdayText { get; } = ContactHistory.WeekdayAndYearText(note);
    public string Title { get; } = ContactHistory.RowTitle(note);
    public string Text { get; } = ContactHistory.RowText(note);
    public string Links { get; } = ContactHistory.RowLinks(note, contactId);
    public bool HasText => Text.Length > 0;
    public bool HasLinks => Links.Length > 0;
    public int Year => ContactHistory.Day(Note).Year;

    /// <summary>"Note, 26 May 2026: Visit. Line of text. Linked: …"</summary>
    public string AutomationName =>
        string.Join(". ", new[]
        {
            $"Note, {ContactHistory.Day(Note):d MMMM yyyy}: {(Title.Length > 0 ? Title : "untitled")}",
            Text,
            HasLinks ? $"Also linked to {Links}" : "",
        }.Where(s => s.Length > 0));

    public RecordRef ToRef() => new(RecordType.Note, Id);
}

/// <summary>The notes of one month, under a month header.</summary>
public sealed class HistoryMonth(string header, IEnumerable<HistoryRow> rows) : List<HistoryRow>(rows)
{
    public string Header { get; } = header;
}

/// <summary>
/// One contact: who it is, how to reach them, its history (the linked notes) and the other records
/// linked to it.
/// </summary>
public sealed class ContactPageViewModel(RecordRef record) : PageViewModel
{
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;

    public Contact? Contact { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsMissing { get; private set; }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public LinkedRecordSet Linked { get; private set; } = LinkedRecordSet.Empty;

    /// <summary>The linked notes, newest first.</summary>
    public IReadOnlyList<HistoryNote> History { get; private set; } = [];

    /// <summary>When the contact was created; null before schema v3.</summary>
    public long? CreatedAt { get; private set; }

    /// <summary>"First Last", or "Contact" when both are blank.</summary>
    public string Name => Contact is { } c && RecordText.ContactFullName(c) is { Length: > 0 } name ? name : RecordType.Contact.DisplayName();

    /// <summary>The first name, or the last name when there is none.</summary>
    public string ShortName => Contact is { } c && ContactPage.ShortName(c) is { Length: > 0 } name ? name : Name;

    public (string Text, bool IsPlaceholder) HeaderLine => Contact is { } c ? ContactPage.HeaderLine(c) : ("", true);

    public CompanyLink Company => Contact is { } c ? ContactPage.Company(c, Linked.Find(RecordType.Organization)?.Records ?? []) : new CompanyLink("", null);

    public IReadOnlyList<ContactChip> Chips => Contact is { } c ? ContactPage.Chips(c) : [];

    public IReadOnlyList<MissingContactFact> MissingFacts => Contact is { } c ? ContactPage.MissingFacts(c) : [];

    /// <summary>The contact's comments, as typed; empty when there are none.</summary>
    public string Comments => string.IsNullOrWhiteSpace(Contact?.Comments) ? "" : Contact!.Comments!.Trim();

    public bool HasComments => Comments.Length > 0;

    /// <summary>A side column section's records: latest linked first.</summary>
    public IReadOnlyList<LinkedRecordSummary> SideRecords(RecordType type) => RecordPageLayout.SideOrder(Linked.Find(type)?.Records ?? []);

    /// <summary>Records in the side column (organizations, categories and items).</summary>
    public int SideCount => ContactPage.SideTypes.Sum(t => Linked.Find(t)?.Count ?? 0);

    /// <summary>Raised after each load, so the page can rebuild.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var contact = await Data.Contacts.GetContactAsync(Record.Id);
            var linked = contact != null ? Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : Task.FromResult(LinkedRecordSet.Empty);
            var history = contact != null ? Data.LinkedRecords.GetLinkedNotesWithLinksAsync(Record.Id) : Task.FromResult(new List<HistoryNote>());
            var createdAt = contact != null ? Data.Contacts.GetCreatedAtAsync(Record.Id) : Task.FromResult<long?>(null);
            await Task.WhenAll(linked, history, createdAt);
            if (version != _loadVersion)
            {
                return;
            }

            Contact = contact;
            IsMissing = contact == null;
            Linked = linked.Result;
            History = ContactHistory.Order(history.Result);
            CreatedAt = createdAt.Result;
            IsLoaded = true;
            if (contact != null && !_addedToRecents)
            {
                _addedToRecents = true;
                App.Settings.AddRecent(RecordType.Contact, Record.Id);
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

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, RecordType.Contact);

    /// <summary>Unlinks a record from the contact. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(string recordId) => Data.UnlinkRecords.InvokeAsync(Record.Id, recordId);

    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Text of the delete confirmation (same wording as the other record pages).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var text = "This contact will be permanently deleted.";
            var count = Linked.TotalCount;
            if (count == 1)
            {
                text += "\n\nThe link to 1 record linked to this contact will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this contact will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }
}
