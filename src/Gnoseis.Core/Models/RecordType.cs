/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Models;

/// <summary>Record type ids as stored in LinkedRecord.record1TypeId / record2TypeId.</summary>
public enum RecordType
{
    Note = 1,
    Contact = 2,
    Organization = 3,
    Category = 4,
    Item = 5,
    LinkedRecord = 5001,
}

public static class RecordTypeExtensions
{
    public static string DisplayName(this RecordType type) => type switch
    {
        RecordType.Note => "Note",
        RecordType.Contact => "Contact",
        RecordType.Organization => "Organization",
        RecordType.Category => "Category",
        RecordType.Item => "Item",
        _ => type.ToString(),
    };

    public static string PluralDisplayName(this RecordType type) => type switch
    {
        RecordType.Note => "Notes",
        RecordType.Contact => "Contacts",
        RecordType.Organization => "Organizations",
        RecordType.Category => "Categories",
        RecordType.Item => "Items",
        _ => type.ToString(),
    };

    /// <summary>The record types a user can create and link.</summary>
    public static IReadOnlyList<RecordType> UserTypes { get; } =
        [RecordType.Note, RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item];
}

/// <summary>Maximum lengths of the title field of each record type (same as Android).</summary>
public static class TitleLength
{
    public const int Category = 50;
    public const int Contact = 50;
    public const int Item = 50;
    public const int Note = 60;
    public const int Organization = 50;
}
