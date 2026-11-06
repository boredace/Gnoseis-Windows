/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using NoteLogic = Gnoseis.Core.Domain.NotePage;

namespace Gnoseis.App.ViewModels;

/// <summary>One note: its title, day and time, its text, when it was created and changed, and the records linked to it.</summary>
public sealed class NotePageViewModel(RecordRef record) : PageViewModel
{
    private int _loadVersion;
    private bool _addedToRecents;

    public RecordRef Record { get; } = record;

    public Note? Note { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool IsMissing { get; private set; }

    public bool IsAvailable => IsLoaded && !IsMissing;

    public LinkedRecordSet Linked { get; private set; } = LinkedRecordSet.Empty;

    /// <summary>When the note was created and last changed; null when unknown.</summary>
    public long? CreatedAt { get; private set; }

    public long? UpdatedAt { get; private set; }

    /// <summary>The title, or "Untitled note" (a placeholder) when there's none.</summary>
    public (string Text, bool IsPlaceholder) Title => Note is { } n ? NoteLogic.Title(n) : (NoteLogic.UntitledText, true);

    public bool HasTitle => !Title.IsPlaceholder;

    /// <summary>"Thu 15 Oct 2026, 09:30".</summary>
    public string DayAndTime => Note is { } n ? NoteLogic.DayAndTimeLine(n) : "";

    /// <summary>The text as stored.</summary>
    public string Text => Note?.TextContents ?? "";

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>"Created … · changed …"; empty when neither time is known.</summary>
    public string TimesCaption => NoteLogic.TimesCaption(CreatedAt, UpdatedAt);

    /// <summary>The name used in messages and the delete confirmation: the title, else the first line, else the day.</summary>
    public string DisplayName => Note is { } n ? RecordRows.NoteTitleAndPreview(n).Title : NoteLogic.UntitledText;

    /// <summary>The side column's sections, contacts first.</summary>
    public IReadOnlyList<(RecordType Type, IReadOnlyList<LinkedRecordSummary> Records)> SideSections => NoteLogic.SideSections(Linked);

    /// <summary>Raised after each load, so the page can rebuild.</summary>
    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var note = await Data.Notes.GetNoteAsync(Record.Id);
            var linked = note != null ? Data.LinkedRecords.GetLinkedRecordSetAsync(Record.Id) : Task.FromResult(LinkedRecordSet.Empty);
            var times = note != null ? Data.Notes.GetTimesAsync(Record.Id) : Task.FromResult<(long?, long?)>((null, null));
            await Task.WhenAll(linked, times);
            if (version != _loadVersion)
            {
                return;
            }

            Note = note;
            IsMissing = note == null;
            Linked = linked.Result;
            (CreatedAt, UpdatedAt) = times.Result;
            IsLoaded = true;
            if (note != null && !_addedToRecents)
            {
                _addedToRecents = true;
                App.Settings.AddRecent(RecordType.Note, Record.Id);
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

    /// <summary>
    /// Saves an edit made in place: only when something changed, through the note update (which
    /// writes the editable columns and moves updatedAt). A new day keeps the time of day.
    /// </summary>
    public async Task SaveAsync(string title, string text, DateOnly day)
    {
        if (Note is { } note && NoteLogic.Edited(note, title, text, day) is { } edited)
        {
            await Data.Notes.UpdateNoteAsync(edited);
        }
    }

    public Task DeleteAsync() => Data.DeleteRecord.InvokeAsync(Record.Id, RecordType.Note);

    /// <summary>Unlinks a record from the note. Returns the removed link (for Undo), or null if they weren't linked.</summary>
    public Task<LinkedRecord?> UnlinkAsync(string recordId) => Data.UnlinkRecords.InvokeAsync(Record.Id, recordId);

    public Task UndoUnlinkAsync(LinkedRecord removed) => Data.UnlinkRecords.UndoAsync(removed);

    /// <summary>Text of the delete confirmation (same wording as the other record pages).</summary>
    public string DeleteConfirmationText
    {
        get
        {
            var text = "This note will be permanently deleted.";
            var count = Linked.TotalCount;
            if (count == 1)
            {
                text += "\n\nThe link to 1 record linked to this note will be removed. The linked record itself will not be deleted.";
            }
            else if (count > 1)
            {
                text += $"\n\nLinks to {count} records linked to this note will be removed. The linked records themselves will not be deleted.";
            }
            return text;
        }
    }
}
