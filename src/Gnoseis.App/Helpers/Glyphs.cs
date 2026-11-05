/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.App.Helpers;

/// <summary>Segoe Fluent Icons glyphs used by the app.</summary>
public static class Glyphs
{
    public const string Home = "";
    public const string Note = "";          // QuickNote
    public const string Contact = "";       // Contact
    public const string Organization = "";  // Work
    public const string Category = "";      // Tag
    public const string Item = "";          // Package
    public const string Details = "";       // Document
    public const string Phone = "";
    public const string Mail = "";
    public const string Link = "";
    public const string Question = "";      // Unknown
    public const string Warning = "";

    public static string For(RecordType type) => type switch
    {
        RecordType.Note => Note,
        RecordType.Contact => Contact,
        RecordType.Organization => Organization,
        RecordType.Category => Category,
        RecordType.Item => Item,
        _ => Question,
    };
}

