/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.ViewModels;

/// <summary>One value on the details tab, e.g. a phone number with its label.</summary>
public sealed class DetailField(string value, string label, Uri? link = null)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public Uri? Link { get; } = link;
    public bool HasLink => Link != null;
    public bool HasNoLink => Link == null;
}

/// <summary>A titled block of fields on the details tab (e.g. "Phone").</summary>
public sealed class DetailSection(string header, string glyph, IReadOnlyList<DetailField> fields)
{
    public string Header { get; } = header;
    public string Glyph { get; } = glyph;
    public IReadOnlyList<DetailField> Fields { get; } = fields;
}

/// <summary>A tab of linked records of one type ("Contacts (3)").</summary>
public sealed class LinkedTab(RecordType type, IReadOnlyList<RecordRow> rows)
{
    public RecordType Type { get; } = type;
    public IReadOnlyList<RecordRow> Rows { get; } = rows;
    public string Header => Type.PluralDisplayName();
    public string CountText => Rows.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string Glyph => Glyphs.For(Type);
    public Microsoft.UI.Xaml.Media.SolidColorBrush TileBrush => TypeColors.BrushFor(Type);
}

/// <summary>Details of one record and the records linked to it (all five Android details pages).</summary>
public sealed class RecordDetailsViewModel(RecordRef record) : PageViewModel
{
    private string _title = "";
    private string _subtitle = "";
    private string _body = "";
    private IReadOnlyList<DetailSection> _sections = [];
    private IReadOnlyList<LinkedTab> _tabs = [];
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

    public string NoDetailsText => Type == RecordType.Note ? "This note has no content" : $"This {TypeName.ToLowerInvariant()} has no details";

    public IReadOnlyList<LinkedTab> Tabs
    {
        get => _tabs;
        private set
        {
            if (SetProperty(ref _tabs, value))
            {
                OnPropertyChanged(nameof(LinkedRecordCount));
                OnPropertyChanged(nameof(HasLinks));
                OnPropertyChanged(nameof(HasNoLinks));
                OnPropertyChanged(nameof(LinkedHeader));
            }
        }
    }

    public int LinkedRecordCount => Tabs.Sum(t => t.Rows.Count);

    public bool HasLinks => LinkedRecordCount > 0;

    public bool HasNoLinks => IsLoaded && LinkedRecordCount == 0;

    public string LinkedHeader => LinkedRecordCount == 0 ? "Linked records" : $"Linked records ({LinkedRecordCount})";

    public bool IsNote => Type == RecordType.Note;

    public bool IsContact => Type == RecordType.Contact;

    public bool ShowTypeTile => !IsContact;

    /// <summary>Heading of the comments card of contacts, organizations, categories and items.</summary>
    public string BodyHeader => Type == RecordType.Contact ? "Comments" : "Description";

    public bool ShowBodyCard => HasBody && !IsNote;

    public bool ShowNoteText => HasBody && IsNote;

    public Microsoft.UI.Xaml.Media.SolidColorBrush TileBrush => TypeColors.BrushFor(Type);

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

    /// <summary>Raised after a load, so the page can rebuild its tab bar.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var found = await LoadRecordAsync();
            var linked = found ? await Data.LinkedRecords.GetLinkedRecordsAsync(Record.Id) : LinkedRecords.Empty;
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
            Tabs = BuildTabs(linked);
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

    public Task UnlinkAsync(RecordRow row) => Data.LinkedRecords.DeleteLinkAsync(Record.Id, row.Id);

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
                Subtitle = RecordRows.FormatNoteDate(note);
                Body = note.TextContents ?? "";
                return true;

            case RecordType.Contact:
                var contact = await Data.Contacts.GetContactAsync(Record.Id);
                if (contact == null)
                {
                    return false;
                }
                Title = contact.DisplayName;
                Subtitle = "";
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
                Body = category.Comments ?? "";
                return true;

            case RecordType.Item:
                var item = await Data.Items.GetItemAsync(Record.Id);
                if (item == null)
                {
                    return false;
                }
                Title = item.ItemName;
                Body = item.Comments ?? "";
                return true;

            default:
                return false;
        }
    }

    private static List<DetailSection> BuildContactSections(Contact contact)
    {
        var sections = new List<DetailSection>();

        var company = Fields((contact.Company, "Company", null), (contact.JobTitle, "Job title", null));
        if (company.Count > 0)
        {
            sections.Add(new DetailSection("Company", Glyphs.Organization, company));
        }

        var phones = Fields(
            (contact.PhoneMain, "Main", "tel:"), (contact.PhoneWork, "Work", "tel:"),
            (contact.PhoneHome, "Home", "tel:"), (contact.PhoneMobile, "Mobile", "tel:"));
        if (phones.Count > 0)
        {
            sections.Add(new DetailSection("Phone", Glyphs.Phone, phones));
        }

        var emails = Fields(
            (contact.EmailMain, "Main", "mailto:"), (contact.EmailWork, "Work", "mailto:"),
            (contact.EmailHome, "Home", "mailto:"), (contact.EmailMobile, "Mobile", "mailto:"));
        if (emails.Count > 0)
        {
            sections.Add(new DetailSection("Email", Glyphs.Mail, emails));
        }

        return sections;
    }

    private static List<DetailField> Fields(params (string? Value, string Label, string? Scheme)[] values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => new DetailField(v.Value!, v.Label, MakeLink(v.Scheme, v.Value!)))
            .ToList();

    private static Uri? MakeLink(string? scheme, string value)
    {
        if (scheme == null)
        {
            return null;
        }
        var target = scheme == "tel:" ? new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray()) : value.Trim();
        return target.Length > 0 && Uri.TryCreate(scheme + target, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static List<LinkedTab> BuildTabs(LinkedRecords linked)
    {
        var tabs = new List<LinkedTab>
        {
            new(RecordType.Note, linked.Notes.Select(n => RecordRows.FromNote(n, mixedList: true)).ToList()),
            new(RecordType.Contact, linked.Contacts.Select(c => RecordRows.FromContact(c, mixedList: true)).ToList()),
            new(RecordType.Organization, linked.Organizations.Select(o => RecordRows.FromOrganization(o, mixedList: true)).ToList()),
            new(RecordType.Category, linked.Categories.Select(c => RecordRows.FromCategory(c, mixedList: true)).ToList()),
            new(RecordType.Item, linked.Items.Select(i => RecordRows.FromItem(i, mixedList: true)).ToList()),
        };
        // Same order as Android: the type with the most links first.
        return tabs.Where(t => t.Rows.Count > 0).OrderByDescending(t => t.Rows.Count).ToList();
    }
}
