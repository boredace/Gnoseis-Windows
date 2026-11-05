/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Domain;

/// <summary>
/// The record a new record will be linked to, with the name shown for it (same rules as Android):
/// a contact's "First Last", the name of an organization, category or item, a note's title or
/// else its day (yyyy-MM-dd). A blank name falls back to the type name.
/// </summary>
public sealed class GetLinkSourceUseCase(
    NoteRepository notes,
    ContactRepository contacts,
    OrganizationRepository organizations,
    CategoryRepository categories,
    ItemRepository items)
{
    /// <summary>The record with its name, or null if it doesn't exist.</summary>
    public async Task<LinkSource?> InvokeAsync(RecordType type, string id)
    {
        string? name = type switch
        {
            RecordType.Contact => await contacts.GetContactAsync(id).ConfigureAwait(false) is { } contact
                ? RecordText.ContactFullName(contact) : null,
            RecordType.Organization => (await organizations.GetOrganizationAsync(id).ConfigureAwait(false))?.OrganizationName,
            RecordType.Category => (await categories.GetCategoryAsync(id).ConfigureAwait(false))?.CategoryName,
            RecordType.Item => (await items.GetItemAsync(id).ConfigureAwait(false))?.ItemName,
            RecordType.Note => await notes.GetNoteAsync(id).ConfigureAwait(false) is { } note
                ? (string.IsNullOrWhiteSpace(note.Title) ? RecordText.NoteDay(note) : note.Title) : null,
            _ => null,
        };
        return name == null ? null : new LinkSource(type, string.IsNullOrWhiteSpace(name) ? type.DisplayName() : name.Trim());
    }
}
