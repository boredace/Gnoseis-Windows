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
/// One category: its name, comments and the five lists of records linked to it (notes, contacts,
/// organizations, items, categories).
/// </summary>
public sealed class CategoryPageViewModel(RecordRef record) : PageViewModel
{
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;

    public Category? Category { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsMissing { get; private set; }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public LinkedRecordSet Linked { get; private set; } = LinkedRecordSet.Empty;

    /// <summary>Each list's rows, in the list's order.</summary>
    public IReadOnlyDictionary<RecordType, IReadOnlyList<CategoryRow>> Rows { get; private set; } =
        new Dictionary<RecordType, IReadOnlyList<CategoryRow>>();

    public IReadOnlyList<CategoryRow> RowsOf(RecordType type) => Rows.TryGetValue(type, out var rows) ? rows : [];

    /// <summary>Records in all five lists together.</summary>
    public int TotalCount => CategoryPage.ListTypes.Sum(t => RowsOf(t).Count);

    private string StoredName => Category?.CategoryName?.Trim() ?? "";

    /// <summary>The name, or "Category" when blank.</summary>
    public string Name => StoredName.Length > 0 ? StoredName : RecordType.Category.DisplayName();

    public (string Text, bool IsPlaceholder) HeaderLine => CategoryPage.HeaderLine(TotalCount);

    /// <summary>The comments, as typed; empty when there are none.</summary>
    public string Comments => (Category?.Comments ?? "").Trim();

    public bool HasComments => Comments.Length > 0;

    public string CopyText => Category is { } category ? CategoryPage.CopyText(category) : "";

    /// <summary>Raised after each load, so the page can rebuild.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var category = await Data.Categories.GetCategoryAsync(Record.Id);
            var exists = category != null;
            var linked = exists ? Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : Task.FromResult(LinkedRecordSet.Empty);
            var notes = exists ? Data.LinkedRecords.GetLinkedNotesWithLinksAsync(Record.Id) : Task.FromResult(new List<HistoryNote>());
            await Task.WhenAll(linked, notes);
            if (version != _loadVersion)
            {
                return;
            }

            Category = category;
            IsMissing = !exists;
            Linked = linked.Result;
            Rows = CategoryPage.Rows(Record.Id, notes.Result, Linked, DateOnly.FromDateTime(DateTime.Now));
            IsLoaded = true;
            if (exists && !_addedToRecents)
            {
                _addedToRecents = true;
                App.Settings.AddRecent(RecordType.Category, Record.Id);
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

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, RecordType.Category);

    /// <summary>Unlinks a record. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(string recordId) => Data.UnlinkRecords.InvokeAsync(Record.Id, recordId);

    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Text of the delete confirmation (same wording as the other record pages).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var text = "This category will be permanently deleted.";
            var count = Linked.TotalCount;
            if (count == 1)
            {
                text += "\n\nThe link to 1 record linked to this category will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this category will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }
}
