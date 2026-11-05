/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.ViewModels;

/// <summary>One value in the Details section, e.g. a phone number with its label.</summary>
public sealed class DetailField(string value, string label, Uri? link = null)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public Uri? Link { get; } = link;
    public bool HasLink => Link != null;
    public bool HasNoLink => Link == null;

    /// <summary>A divider above the field: every field of a group but the first.</summary>
    public bool HasDivider { get; init; }

    public Microsoft.UI.Xaml.Thickness DividerThickness => new(0, HasDivider ? 1 : 0, 0, 0);
}

/// <summary>A titled group of fields in the Details section (e.g. "Phone"), shown as one card.</summary>
public sealed class DetailSection(string header, IReadOnlyList<DetailField> fields)
{
    public string Header { get; } = header;
    public IReadOnlyList<DetailField> Fields { get; } = fields;
}

/// <summary>Details of one record and the records linked to it (all five Android details pages).</summary>
public sealed class RecordDetailsViewModel(RecordRef record) : PageViewModel
{
    private string _title = "";
    private string _subtitle = "";
    private string _body = "";
    private IReadOnlyList<DetailSection> _sections = [];
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

    public IReadOnlyList<DetailSection> Sections
    {
        get => _sections;
        private set
        {
            if (SetProperty(ref _sections, value))
            {
                OnPropertyChanged(nameof(HasSections));
                OnPropertyChanged(nameof(HasNoDetails));
            }
        }
    }

    public bool HasSections => Sections.Count > 0;

    public bool HasNoDetails => IsLoaded && !HasBody && !HasSections;

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

    public bool IsContact => Type == RecordType.Contact;

    public bool ShowTypeTile => !IsContact;

    /// <summary>Heading of the comments card of contacts, organizations, categories and items.</summary>
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
                Sections = [];
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

            case RecordType.Contact:
                var contact = await Data.Contacts.GetContactAsync(Record.Id);
                if (contact == null)
                {
                    return false;
                }
                Title = RecordText.ContactFullName(contact) is { Length: > 0 } name ? name : TypeName;
                Subtitle = RecordText.JobAndCompany(contact);
                Sections = BuildContactSections(contact);
                Body = contact.Comments ?? "";
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

    private static List<DetailSection> BuildContactSections(Contact contact)
    {
        var sections = new List<DetailSection>();
        AddSection(sections, "Work", (contact.Company, "Company", null), (contact.JobTitle, "Job title", null));
        AddSection(sections, "Phone",
            (contact.PhoneMain, "Main", "tel:"), (contact.PhoneWork, "Work", "tel:"),
            (contact.PhoneHome, "Home", "tel:"), (contact.PhoneMobile, "Mobile", "tel:"));
        AddSection(sections, "E-mail",
            (contact.EmailMain, "Main", "mailto:"), (contact.EmailWork, "Work", "mailto:"),
            (contact.EmailHome, "Home", "mailto:"), (contact.EmailMobile, "Mobile", "mailto:"));
        return sections;
    }

    /// <summary>Adds a group with the values that aren't blank; an empty group is left out.</summary>
    private static void AddSection(List<DetailSection> sections, string header, params (string? Value, string Label, string? Scheme)[] values)
    {
        var fields = values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select((v, index) => new DetailField(v.Value!.Trim(), v.Label, MakeLink(v.Scheme, v.Value!)) { HasDivider = index > 0 })
            .ToList();
        if (fields.Count > 0)
        {
            sections.Add(new DetailSection(header, fields));
        }
    }

    private static Uri? MakeLink(string? scheme, string value)
    {
        if (scheme == null)
        {
            return null;
        }
        var target = scheme == "tel:" ? new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray()) : value.Trim();
        return target.Length > 0 && Uri.TryCreate(scheme + target, UriKind.Absolute, out var uri) ? uri : null;
    }
}
