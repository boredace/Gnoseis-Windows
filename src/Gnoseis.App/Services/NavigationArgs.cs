/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.Services;

/// <summary>Identifies one record.</summary>
public sealed record RecordRef(RecordType Type, string Id);

/// <summary>
/// What an editor edits. <see cref="Id"/> is null for a new record. When <see cref="LinkFrom"/> is
/// set, the new record is linked to that record after it is saved (the Android NEWLINK mode).
/// <see cref="PrefillName"/> is a search query to fill in as a new record's name (creating a record
/// from a search); how it is split and cut per type is in CreateFromSearch. <see cref="NoteDraft"/>
/// fills in a new note (a draft carried over from the contact page's note box), and
/// <see cref="FocusField"/> names the field that gets the focus when the editor opens.
/// </summary>
public sealed record EditRequest(
    RecordType Type,
    string? Id = null,
    RecordRef? LinkFrom = null,
    string? PrefillName = null,
    NoteDraft? NoteDraft = null,
    string? FocusField = null)
{
    public bool IsNew => Id == null;
}

/// <summary>A new note's title, text, day and time, written before the editor opened.</summary>
public sealed record NoteDraft(string Title, string Text, DateOnly Day, TimeSpan TimeOfDay);

/// <summary>A page that shows a record in a section's detail pane.</summary>
public interface IRecordPage
{
    /// <summary>The record shown.</summary>
    RecordRef Record { get; }

    /// <summary>Asks, then deletes the record (also used by the Delete key in the list).</summary>
    Task DeleteWithConfirmationAsync();
}

/// <summary>Parameter of a section page: its record type, the record to show and an editor to open.</summary>
public sealed record SectionArgs(RecordType Type, string? SelectedId = null, EditRequest? Edit = null);

/// <summary>Parameter of the search page: the query, and the section searched from (its type's chip starts selected).</summary>
public sealed record SearchArgs(string Query, RecordType? Scope = null);

/// <summary>Implemented by pages that may need to confirm before the user leaves them.</summary>
public interface IConfirmLeave
{
    Task<bool> CanLeaveAsync();
}

/// <summary>
/// A section page: a list of records with the selected record (or an editor) shown next to it.
/// </summary>
public interface ISectionHost
{
    RecordType Type { get; }

    /// <summary>Selects a record in the list and shows it.</summary>
    Task ShowRecordAsync(string id);

    /// <summary>Shows an editor in the detail pane.</summary>
    Task ShowEditorAsync(EditRequest request);

    /// <summary>Closes the editor after save (<paramref name="saved"/> set) or cancel.</summary>
    Task CloseEditorAsync(EditRequest request, RecordRef? saved);

    /// <summary>The record shown in the detail pane was deleted.</summary>
    void OnRecordDeleted(string id);

    /// <summary>True when "back" would be handled inside the section (a trail open, or a narrow window showing a record).</summary>
    bool CanHandleBack { get; }

    /// <summary>Handles "back" inside the section (a step along the trail, or from the record to the list in a narrow window). Returns true if handled.</summary>
    Task<bool> TryHandleBackAsync();

    /// <summary>
    /// The records opened from one another in this section: the record selected in the list first,
    /// then each record opened from a link on the record above it.
    /// </summary>
    Gnoseis.Core.Domain.RecordTrail<RecordRef> Trail { get; }

    /// <summary>True while the section is narrow: the trail is then a breadcrumb above the record.</summary>
    bool IsNarrow { get; }

    /// <summary>Raised when the section crosses the narrow width.</summary>
    event EventHandler? NarrowChanged;

    /// <summary>The title the trail shows for one of its records, if it's loaded.</summary>
    string? TrailTitleOf(RecordRef record);

    /// <summary>Opens a record from a link on the record shown, adding it to the trail.</summary>
    Task OpenLinkedRecordAsync(RecordRef record);

    /// <summary>One step forward along the trail. Returns true if there was one.</summary>
    Task<bool> TryGoForwardAsync();

    /// <summary>Closes the trail: the list again, with the trail's first record selected.</summary>
    Task CloseTrailAsync();
}
