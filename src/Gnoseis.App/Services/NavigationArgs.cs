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
/// </summary>
public sealed record EditRequest(RecordType Type, string? Id = null, RecordRef? LinkFrom = null)
{
    public bool IsNew => Id == null;
}

/// <summary>Parameter of a section page: its record type, the record to show and an editor to open.</summary>
public sealed record SectionArgs(RecordType Type, string? SelectedId = null, EditRequest? Edit = null);

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

    /// <summary>True when "back" would be handled inside the section (narrow window showing a record).</summary>
    bool CanHandleBack { get; }

    /// <summary>Handles "back" inside the section (e.g. narrow window showing a record). Returns true if handled.</summary>
    Task<bool> TryHandleBackAsync();
}
