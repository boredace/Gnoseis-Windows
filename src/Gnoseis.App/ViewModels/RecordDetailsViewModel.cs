/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.ViewModels;

/// <summary>Details of a note, organization, category or item and the records linked to it (contacts have their own page).</summary>
public sealed class RecordDetailsViewModel(RecordRef record) : PageViewModel
{
    private string _title = "";
    private string _subtitle = "";
    private string _body = "";
    private LinkedRecordSet _linked = LinkedRecordSet.Empty;
    private bool _isMissing;
    private bool _isLoaded;
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;
    public RecordType Type => Record.Type;
    public string TypeName => Type.DisplayName();
    public string Glyph => Glyphs.For(Type);

    /// <summary>Types of new records that can be created and linked from here (all but this type, as on Android).</summary>
    public IReadOnlyList<RecordType> NewLinkedTypes => RecordTypeExtensions.UserTypes.Where(t => t != Type).ToList();

    public string Title { get => _title; private set => SetProperty(ref _title, value); }

    public string Subtitle
    {
        get => _subtitle;
        private set
        {
            if (SetProperty(ref _subtitle, value))
            {
                OnPropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>Note text or record comments.</summary>
    public string Body
    {
        get => _body;
        private set
        {
            if (SetProperty(ref _body, value))
            {
                OnPropertyChanged(nameof(HasBody));
                OnPropertyChanged(nameof(HasNoDetails));
                OnPropertyChanged(nameof(ShowBodyCard));
                OnPropertyChanged(nameof(ShowNoteText));
            }
        }
    }

    public bool HasBody => Body.Length > 0;

    public bool HasNoDetails => IsLoaded && !HasBody;

    public string NoDetailsText => "No details";

    /// <summary>The linked records by type, sorted by count (most first).</summary>
    public LinkedRecordSet Linked
    {
        get => _linked;
        private set
        {
            _linked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkedRecordCount));
            OnPropertyChanged(nameof(HasLinks));
            OnPropertyChanged(nameof(HasNoLinks));
        }
    }

    public int LinkedRecordCount => Linked.TotalCount;

    public bool HasLinks => LinkedRecordCount > 0;

    public bool HasNoLinks => IsLoaded && LinkedRecordCount == 0;

    public bool IsNote => Type == RecordType.Note;

    /// <summary>Heading of the comments card of organizations, categories and items.</summary>
    public string BodyHeader => "Comments";

    public bool ShowBodyCard => HasBody && !IsNote;

    public bool ShowNoteText => HasBody && IsNote;

    public bool IsMissing
    {
        get => _isMissing;
        private set
        {
            if (SetProperty(ref _isMissing, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
            }
        }
    }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public bool IsLoaded
    {
        get => _isLoaded;
        private set
        {
            if (SetProperty(ref _isLoaded, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(HasNoDetails));
                OnPropertyChanged(nameof(HasNoLinks));
            }
        }
    }

    /// <summary>Raised after a load, so the page can rebuild its selector and linked records.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var found = await LoadRecordAsync();
            var linked = found ? await Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : LinkedRecordSet.Empty;
            if (version != _loadVersion)
            {
                return;
            }

            IsMissing = !found;
            if (!found)
            {
                Title = $"{TypeName} not found";
                Subtitle = "";
                Body = "";
            }
            Linked = linked;
            IsLoaded = true;
            if (found && !_addedToRecents)
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

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, Type);

    /// <summary>Unlinks the row's record from this one. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(LinkedRow row) => Data.UnlinkRecords.InvokeAsync(Record.Id, row.Id);

    /// <summary>Undo of <see cref="UnlinkAsync"/>: links the two records again, the same way round.</summary>
    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Text of the delete confirmation (same wording as the Android dialog).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var name = TypeName.ToLowerInvariant();
            var text = $"This {name} will be permanently deleted.";
            var count = LinkedRecordCount;
            if (count == 1)
            {
                text += $"\n\nThe link to 1 record linked to this {name} will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this {name} will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e) => _ = LoadAsync();

    private async Task<bool> LoadRecordAsync()
    {
        switch (Type)
        {
            case RecordType.Note:
                var note = await Data.Notes.GetNoteAsync(Record.Id);
                if (note == null)
                {
                    return false;
                }
                Title = RecordRows.NoteTitleAndPreview(note).Title;
                Subtitle = RecordRows.NoteDayText(note);
                Body = note.TextContents ?? "";
                return true;

            case RecordType.Organization:
                var organization = await Data.Organizations.GetOrganizationAsync(Record.Id);
                if (organization == null)
                {
                    return false;
                }
                Title = organization.OrganizationName;
                Body = organization.Comments ?? "";
                return true;

            case RecordType.Category:
                var category = await Data.Categories.GetCategoryAsync(Record.Id);
                if (category == null)
                {
                    return false;
                }
                Title = category.CategoryName;
                Subtitle = await Data.Categories.GetParentPathAsync(category);
                Body = category.Comments ?? "";
                return true;

            case RecordType.Item:
                var item = await Data.Items.GetItemAsync(Record.Id);
                if (item == null)
                {
                    return false;
                }
                Title = item.ItemName;
                Subtitle = await Data.Items.GetParentPathAsync(item);
                Body = item.Comments ?? "";
                return true;

            default:
                return false;
        }
    }
}
