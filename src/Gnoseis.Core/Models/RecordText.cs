/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>Short forms of stored text for rows and names.</summary>
public static class RecordText
{
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The first line that isn't blank, trimmed; empty if there is none.</summary>
    public static string FirstLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    /// <summary>All lines that aren't blank, trimmed and joined by spaces.</summary>
    public static string OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : string.Join(" ", text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>"First Last", trimmed (Android's contact name for the link source).</summary>
    public static string ContactFullName(Contact contact) =>
        string.Join(" ", new[] { contact.NameFirst, contact.NameLast }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

    /// <summary>"Job title · company", or either one alone.</summary>
    public static string JobAndCompany(Contact contact) =>
        string.Join(" · ", new[] { contact.JobTitle, contact.Company }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

    /// <summary>The main phone, or the main e-mail if there is no phone.</summary>
    public static string MainContact(Contact contact) =>
        !string.IsNullOrWhiteSpace(contact.PhoneMain) ? contact.PhoneMain.Trim()
        : !string.IsNullOrWhiteSpace(contact.EmailMain) ? contact.EmailMain.Trim()
        : "";

    /// <summary>A note's day as yyyy-MM-dd: its stored day (createDate), read in UTC, as the note list shows it.</summary>
    public static string NoteDay(Note note) =>
        EpochTime.ToUtcDate(note.CreateDate).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
