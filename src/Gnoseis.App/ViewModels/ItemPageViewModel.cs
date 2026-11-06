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
/// One item: its name, comments, history (the linked notes) and the other records
/// linked to it.
/// </summary>
public sealed class ItemPageViewModel(RecordRef record) : PageViewModel
{
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;

    public Item? Item { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsMissing { get; private set; }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public LinkedRecordSet Linked { get; private set; } = LinkedRecordSet.Empty;

    /// <summary>The linked notes, newest first.</summary>
    public IReadOnlyList<HistoryNote> History { get; private set; } = [];

    /// <summary>The item's name, or "Item" when blank.</summary>
    public string Name => Item is { } i && i.ItemName?.Length > 0 ? i.ItemName : RecordType.Item.DisplayName();

    /// <summary>The item's name, or "Item" when blank.</summary>
    public string ShortName => Name;

    /// <summary>The header line: "Item · 26 notes · 16 linked" or "Item · no notes or links yet".</summary>
    public (string Text, bool IsPlaceholder) HeaderLine
    {
        get
        {
            var noteCount = Linked.Find(RecordType.Note)?.Count ?? 0;
            var linkedCount = Linked.TotalCount;
            return ItemPage.HeaderLine(noteCount, linkedCount);
        }
    }

    /// <summary>The item's comments, as typed; empty when there are none.</summary>
    public string Comments => string.IsNullOrWhiteSpace(Item?.Comments) ? "" : Item!.Comments!.Trim();

    public bool HasComments => Comments.Length > 0;

    /// <summary>A side column section's records: latest linked first.</summary>
    public IReadOnlyList<LinkedRecordSummary> SideRecords(RecordType type) => RecordPageLayout.SideOrder(Linked.Find(type)?.Records ?? []);

    /// <summary>Records in the side column (contacts, organizations, categories and other items).</summary>
    public int SideCount => ItemPage.SideTypes.Sum(t => Linked.Find(t)?.Count ?? 0);

    /// <summary>Raised after each load, so the page can rebuild.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var item = await Data.Items.GetItemAsync(Record.Id);
            var linked = item != null ? Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : Task.FromResult(LinkedRecordSet.Empty);
            var history = item != null ? Data.LinkedRecords.GetLinkedNotesWithLinksAsync(Record.Id) : Task.FromResult(new List<HistoryNote>());
            await Task.WhenAll(linked, history);
            if (version != _loadVersion)
            {
                return;
            }

            Item = item;
            IsMissing = item == null;
            Linked = linked.Result;
            History = ContactHistory.Order(history.Result);
            IsLoaded = true;
            if (item != null && !_addedToRecents)
            {
                _addedToRecents = true;
                App.Settings.AddRecent(RecordType.Item, Record.Id);
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

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, RecordType.Item);

    /// <summary>Unlinks a record from the item. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(string recordId) => Data.UnlinkRecords.InvokeAsync(Record.Id, recordId);

    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Text of the delete confirmation (same wording as the other record pages).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var text = "This item will be permanently deleted.";
            var count = Linked.TotalCount;
            if (count == 1)
            {
                text += "\n\nThe link to 1 record linked to this item will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this item will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }
}
