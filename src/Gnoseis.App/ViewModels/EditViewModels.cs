/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>A text box value with an optional (non-blocking) validation message.</summary>
public sealed class FormField(string label, int maxLength = 0, Func<string, ValidationResult>? validator = null) : BindableBase
{
    private string _value = "";
    private string _error = "";

    public string Label { get; } = label;
    public int MaxLength { get; } = maxLength;

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? ""))
            {
                Validate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => Error.Length > 0;

    /// <summary>The trimmed value, or null when empty (optional database columns are NULL, not "").</summary>
    public string? ValueOrNull => string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();

    public event EventHandler? ValueChanged;

    private void Validate()
    {
        var result = validator != null && !string.IsNullOrWhiteSpace(Value) ? validator(Value.Trim()) : null;
        Error = result is { Successful: false } ? result.ErrorMessage ?? "Not valid" : "";
    }
}

/// <summary>Shared behaviour of the edit pages: new/edit mode, validity, unsaved changes, save and link.</summary>
public abstract class EditViewModelBase : PageViewModel
{
    private bool _isValid;
    private bool _isSaving;
    private string _savedSnapshot = "";

    protected EditViewModelBase(EditRequest request)
    {
        Request = request;
    }

    public EditRequest Request { get; }
    public bool IsNew => Request.IsNew;
    public RecordType Type => Request.Type;
    public string PageTitle => $"{(IsNew ? "New" : "Edit")} {Type.DisplayName().ToLowerInvariant()}";
    public string Glyph => Glyphs.For(Type);

    public bool IsValid
    {
        get => _isValid;
        protected set
        {
            if (SetProperty(ref _isValid, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => IsValid && !IsSaving;

    public bool IsDirty => Snapshot() != _savedSnapshot;

    /// <summary>Loads the record being edited. Returns false if it no longer exists.</summary>
    public async Task<bool> LoadAsync()
    {
        var found = IsNew || await LoadRecordAsync();
        UpdateValidity();
        _savedSnapshot = Snapshot();
        return found;
    }

    /// <summary>
    /// Saves the record. For a new record opened from another record's page, also links it to
    /// that record. Returns the saved record.
    /// </summary>
    public async Task<RecordRef?> SaveAsync()
    {
        UpdateValidity();
        if (!CanSave)
        {
            return null;
        }

        IsSaving = true;
        try
        {
            string id;
            if (IsNew)
            {
                id = await InsertAsync();
                if (Request.LinkFrom is { } source)
                {
                    await Data.LinkedRecords.AddLinkedRecordAsync(new LinkedRecord
                    {
                        OwnerDbId = "db1",
                        Record1Id = source.Id,
                        Record2Id = id,
                        Record1TypeId = (int)source.Type,
                        Record2TypeId = (int)Type,
                    });
                }
            }
            else
            {
                await UpdateAsync();
                id = Request.Id!;
            }
            _savedSnapshot = Snapshot();
            return new RecordRef(Type, id);
        }
        finally
        {
            IsSaving = false;
        }
    }

    protected abstract Task<bool> LoadRecordAsync();
    protected abstract Task<string> InsertAsync();
    protected abstract Task UpdateAsync();
    protected abstract bool Validate();

    /// <summary>All edited values joined, used to detect unsaved changes.</summary>
    protected abstract string Snapshot();

    protected void UpdateValidity() => IsValid = Validate();
}

/// <summary>New / edit note (Android NoteEditPage).</summary>
public sealed class NoteEditViewModel : EditViewModelBase
{
    private Note? _note;
    private DateTimeOffset? _date;

    public NoteEditViewModel(EditRequest request) : base(request)
    {
        NoteTitle.ValueChanged += (_, _) => UpdateValidity();
        Text.ValueChanged += (_, _) => UpdateValidity();
        _date = new DateTimeOffset(DateTime.Today);
    }

    public FormField NoteTitle { get; } = new("Title", TitleLength.Note);
    public FormField Text { get; } = new("Note text");

    /// <summary>The day the note belongs to (Android stores it as UTC midnight of that day).</summary>
    public DateTimeOffset? Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    private DateOnly SelectedDay => DateOnly.FromDateTime((Date ?? DateTimeOffset.Now).Date);

    protected override async Task<bool> LoadRecordAsync()
    {
        _note = await Data.Notes.GetNoteAsync(Request.Id!);
        if (_note == null)
        {
            return false;
        }
        NoteTitle.Value = _note.Title ?? "";
        Text.Value = _note.TextContents ?? "";
        Date = new DateTimeOffset(NoteDates.DisplayedDay(_note).ToDateTime(TimeOnly.MinValue));
        return true;
    }

    protected override async Task<string> InsertAsync()
    {
        var (createDate, createDateTime) = NoteDates.ForNewNote(SelectedDay, DateTime.Now.TimeOfDay);
        var note = await Data.Notes.AddNoteAsync(new Note
        {
            OwnerDbId = "db1",
            Title = NoteTitle.Value,
            TextContents = Text.Value,
            CreateDate = createDate,
            CreateDateTime = createDateTime,
        });
        return note.Id;
    }

    protected override async Task UpdateAsync()
    {
        var (createDate, createDateTime) = NoteDates.ForEditedNote(_note!, SelectedDay);
        await Data.Notes.UpdateNoteAsync(_note! with
        {
            Title = NoteTitle.Value,
            TextContents = Text.Value,
            CreateDate = createDate,
            CreateDateTime = createDateTime,
        });
    }

    protected override bool Validate() => NoteTitle.Value.Length > 0 || Text.Value.Length > 0;

    protected override string Snapshot() => $"{NoteTitle.Value}\u0001{Text.Value}\u0001{SelectedDay}";
}

/// <summary>New / edit contact (Android ContactEditPage).</summary>
public sealed class ContactEditViewModel : EditViewModelBase
{
    private Contact? _contact;

    public ContactEditViewModel(EditRequest request) : base(request)
    {
        NameLast.ValueChanged += (_, _) => UpdateValidity();
    }

    public FormField NameFirst { get; } = new("First name");
    public FormField NameLast { get; } = new("Last name", TitleLength.Contact);
    public FormField JobTitle { get; } = new("Job title");
    public FormField Company { get; } = new("Company");
    public FormField PhoneMain { get; } = new("Main phone", validator: ValidatePhone.Execute);
    public FormField PhoneMobile { get; } = new("Mobile phone", validator: ValidatePhone.Execute);
    public FormField PhoneHome { get; } = new("Home phone", validator: ValidatePhone.Execute);
    public FormField PhoneWork { get; } = new("Work phone", validator: ValidatePhone.Execute);
    public FormField EmailMain { get; } = new("Main email", validator: ValidateEmail.Execute);
    public FormField EmailMobile { get; } = new("Mobile email", validator: ValidateEmail.Execute);
    public FormField EmailHome { get; } = new("Home email", validator: ValidateEmail.Execute);
    public FormField EmailWork { get; } = new("Work email", validator: ValidateEmail.Execute);
    public FormField Comments { get; } = new("Comments");

    private FormField[] AllFields =>
    [
        NameFirst, NameLast, JobTitle, Company, PhoneMain, PhoneMobile, PhoneHome, PhoneWork,
        EmailMain, EmailMobile, EmailHome, EmailWork, Comments,
    ];

    protected override async Task<bool> LoadRecordAsync()
    {
        _contact = await Data.Contacts.GetContactAsync(Request.Id!);
        if (_contact == null)
        {
            return false;
        }
        NameFirst.Value = _contact.NameFirst ?? "";
        NameLast.Value = _contact.NameLast;
        JobTitle.Value = _contact.JobTitle ?? "";
        Company.Value = _contact.Company ?? "";
        PhoneMain.Value = _contact.PhoneMain ?? "";
        PhoneMobile.Value = _contact.PhoneMobile ?? "";
        PhoneHome.Value = _contact.PhoneHome ?? "";
        PhoneWork.Value = _contact.PhoneWork ?? "";
        EmailMain.Value = _contact.EmailMain ?? "";
        EmailMobile.Value = _contact.EmailMobile ?? "";
        EmailHome.Value = _contact.EmailHome ?? "";
        EmailWork.Value = _contact.EmailWork ?? "";
        Comments.Value = _contact.Comments ?? "";
        return true;
    }

    protected override async Task<string> InsertAsync() =>
        (await Data.Contacts.AddContactAsync(Apply(new Contact { OwnerDbId = "db1" }))).Id;

    protected override Task UpdateAsync() => Data.Contacts.UpdateContactAsync(Apply(_contact!));

    private Contact Apply(Contact contact) => contact with
    {
        NameLast = NameLast.Value.Trim(),
        NameFirst = NameFirst.ValueOrNull,
        JobTitle = JobTitle.ValueOrNull,
        Company = Company.ValueOrNull,
        PhoneMain = PhoneMain.ValueOrNull,
        PhoneMobile = PhoneMobile.ValueOrNull,
        PhoneHome = PhoneHome.ValueOrNull,
        PhoneWork = PhoneWork.ValueOrNull,
        EmailMain = EmailMain.ValueOrNull,
        EmailMobile = EmailMobile.ValueOrNull,
        EmailHome = EmailHome.ValueOrNull,
        EmailWork = EmailWork.ValueOrNull,
        Comments = string.IsNullOrWhiteSpace(Comments.Value) ? null : Comments.Value,
    };

    // A contact only needs a last name (Android ContactEditViewModel).
    protected override bool Validate() => NameLast.Value.Trim().Length is >= 1 and <= TitleLength.Contact;

    protected override string Snapshot() => string.Join("\u0001", AllFields.Select(f => f.Value));
}

/// <summary>New / edit organization, category or item: a name and a description.</summary>
public sealed class NamedRecordEditViewModel : EditViewModelBase
{
    private Organization? _organization;
    private Category? _category;
    private Item? _item;

    public NamedRecordEditViewModel(EditRequest request) : base(request)
    {
        var typeName = Type.DisplayName();
        var maxLength = Type switch
        {
            RecordType.Organization => TitleLength.Organization,
            RecordType.Category => TitleLength.Category,
            _ => TitleLength.Item,
        };
        Name = new FormField($"{typeName} name", maxLength);
        Description = new FormField($"{typeName} description");
        Name.ValueChanged += (_, _) => UpdateValidity();
    }

    public FormField Name { get; }
    public FormField Description { get; }

    protected override async Task<bool> LoadRecordAsync()
    {
        switch (Type)
        {
            case RecordType.Organization:
                _organization = await Data.Organizations.GetOrganizationAsync(Request.Id!);
                if (_organization == null)
                {
                    return false;
                }
                Name.Value = _organization.OrganizationName;
                Description.Value = _organization.Comments ?? "";
                return true;
            case RecordType.Category:
                _category = await Data.Categories.GetCategoryAsync(Request.Id!);
                if (_category == null)
                {
                    return false;
                }
                Name.Value = _category.CategoryName;
                Description.Value = _category.Comments ?? "";
                return true;
            default:
                _item = await Data.Items.GetItemAsync(Request.Id!);
                if (_item == null)
                {
                    return false;
                }
                Name.Value = _item.ItemName;
                Description.Value = _item.Comments ?? "";
                return true;
        }
    }

    private string NameValue => Name.Value.Trim();
    private string? DescriptionValue => string.IsNullOrWhiteSpace(Description.Value) ? null : Description.Value;

    protected override async Task<string> InsertAsync() => Type switch
    {
        RecordType.Organization => (await Data.Organizations.AddOrganizationAsync(
            new Organization { OwnerDbId = "db1", OrganizationName = NameValue, Comments = DescriptionValue })).Id,
        RecordType.Category => (await Data.Categories.AddCategoryAsync(
            new Category { OwnerDbId = "db1", CategoryName = NameValue, Comments = DescriptionValue })).Id,
        _ => (await Data.Items.AddItemAsync(
            new Item { OwnerDbId = "db1", ItemName = NameValue, Comments = DescriptionValue })).Id,
    };

    protected override Task UpdateAsync() => Type switch
    {
        RecordType.Organization => Data.Organizations.UpdateOrganizationAsync(
            _organization! with { OrganizationName = NameValue, Comments = DescriptionValue }),
        RecordType.Category => Data.Categories.UpdateCategoryAsync(
            _category! with { CategoryName = NameValue, Comments = DescriptionValue }),
        _ => Data.Items.UpdateItemAsync(_item! with { ItemName = NameValue, Comments = DescriptionValue }),
    };

    protected override bool Validate() => NameValue.Length > 0;

    protected override string Snapshot() => $"{Name.Value}\u0001{Description.Value}";
}
