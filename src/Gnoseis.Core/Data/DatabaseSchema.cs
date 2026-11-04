/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.Core.Data;

/// <summary>
/// The database schema, copied from the Android Room schema export
/// (Gnoseis-Android/app/schemas/org.gnoseis.data.database.GnoseisDatabase/3.json).
/// A database created by this app can be opened by the Android app and vice versa, as long as
/// <see cref="Version"/> and <see cref="RoomIdentityHash"/> match what Room expects.
/// </summary>
public static class DatabaseSchema
{
    /// <summary>Room database version, stored in PRAGMA user_version.</summary>
    public const int Version = 3;

    /// <summary>
    /// The oldest version this app opens. Older supported versions are migrated to
    /// <see cref="Version"/> when the file is opened or restored.
    /// </summary>
    public const int OldestSupportedVersion = 1;

    /// <summary>Room identity hash of <see cref="Version"/>, stored in room_master_table.</summary>
    public const string RoomIdentityHash = "d4a4ee67bfc185f77bdb2d64f54028c7";

    /// <summary>
    /// Room identity hash of versions 1 and 2. They have the same tables, so they share it.
    /// </summary>
    public const string Version2RoomIdentityHash = "e7731af7b4c86f23b4635d9383faf3cc";

    /// <summary>The identity hash Room expects in a file at the given schema version.</summary>
    public static string RoomIdentityHashFor(long version) =>
        version >= 3 ? RoomIdentityHash : Version2RoomIdentityHash;

    /// <summary>Android stores the database as "gnoseis_data" (no extension) in the app's databases folder.</summary>
    public const string FileName = "gnoseis_data";

    /// <summary>Settings table row ids (fixed, same as Android).</summary>
    public static class SettingIds
    {
        /// <summary>Reserved for the restore marker. Never written by this app.</summary>
        public const int AppId = 1;
        public const int LastBackupAt = 2;
        public const int LastDeletionAt = 3;
    }

    internal static readonly string[] CreateStatements =
    [
        "CREATE TABLE IF NOT EXISTS `Category` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `parentId` TEXT, `categoryName` TEXT NOT NULL, `comments` TEXT, `createdAt` INTEGER, `updatedAt` INTEGER, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Contact` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `nameLast` TEXT NOT NULL, `nameFirst` TEXT, `jobTitle` TEXT, `company` TEXT, `phoneMain` TEXT, `phoneMobile` TEXT, `phoneHome` TEXT, `phoneWork` TEXT, `emailMain` TEXT, `emailMobile` TEXT, `emailHome` TEXT, `emailWork` TEXT, `comments` TEXT, `createdAt` INTEGER, `updatedAt` INTEGER, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Database` (`id` TEXT NOT NULL, `name` TEXT NOT NULL, `type` INTEGER NOT NULL, `requestSentDateTime` INTEGER NOT NULL, `requestReceivedDateTIme` INTEGER NOT NULL, `requestAcceptedDateTine` INTEGER NOT NULL, `keyPrivate` TEXT NOT NULL, `keyPublic` TEXT NOT NULL, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Incoming` (`id` TEXT NOT NULL, `dateReceived` INTEGER NOT NULL, `relayMessageContent` TEXT NOT NULL, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Item` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `parentId` TEXT, `itemName` TEXT NOT NULL, `comments` TEXT, `createdAt` INTEGER, `updatedAt` INTEGER, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `LinkedRecord` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `record1Id` TEXT NOT NULL, `record2Id` TEXT NOT NULL, `record1TypeId` INTEGER NOT NULL, `record2TypeId` INTEGER NOT NULL, `createdAt` INTEGER, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Note` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `title` TEXT, `textContents` TEXT, `createDateTime` INTEGER NOT NULL, `createDate` INTEGER NOT NULL, `createdAt` INTEGER, `updatedAt` INTEGER, PRIMARY KEY(`id`))",
        "CREATE TABLE IF NOT EXISTS `Organization` (`id` TEXT NOT NULL, `ownerDbId` TEXT NOT NULL, `organizationName` TEXT NOT NULL, `comments` TEXT, `createdAt` INTEGER, `updatedAt` INTEGER, PRIMARY KEY(`id`))",
        SettingsCreateStatement,
        "CREATE TABLE IF NOT EXISTS `SharedRecord` (`id` TEXT NOT NULL, `sharedWithDbId` TEXT NOT NULL, `shareOwnerDbId` TEXT NOT NULL, `recordId` TEXT NOT NULL, `recordTypeId` INTEGER NOT NULL, `explicitShare` INTEGER NOT NULL, `alsoShareLinked` INTEGER NOT NULL, PRIMARY KEY(`id`))",
        // Room setup queries
        "CREATE TABLE IF NOT EXISTS room_master_table (id INTEGER PRIMARY KEY,identity_hash TEXT)",
        $"INSERT OR REPLACE INTO room_master_table (id,identity_hash) VALUES(42, '{RoomIdentityHash}')",
    ];

    internal const string SettingsCreateStatement =
        "CREATE TABLE IF NOT EXISTS `Settings` (`settingId` INTEGER NOT NULL, `settingName` TEXT NOT NULL, `valueInt` INTEGER, `valueText` TEXT, PRIMARY KEY(`settingId`))";

    /// <summary>Tables that get created and updated times in version 3.</summary>
    internal static readonly string[] TimestampedRecordTables = ["Note", "Contact", "Organization", "Category", "Item"];

    /// <summary>
    /// Columns every table must have at the given schema version. Used to check files before
    /// they are opened or restored.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> RequiredColumnsFor(long version) =>
        version >= 3 ? RequiredColumns : Version2RequiredColumns;

    /// <summary>Columns of version 3 (<see cref="Version"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Category"] = ["id", "ownerDbId", "parentId", "categoryName", "comments", "createdAt", "updatedAt"],
        ["Contact"] = ["id", "ownerDbId", "nameLast", "nameFirst", "jobTitle", "company", "phoneMain", "phoneMobile", "phoneHome", "phoneWork", "emailMain", "emailMobile", "emailHome", "emailWork", "comments", "createdAt", "updatedAt"],
        ["Database"] = ["id", "name", "type", "requestSentDateTime", "requestReceivedDateTIme", "requestAcceptedDateTine", "keyPrivate", "keyPublic"],
        ["Incoming"] = ["id", "dateReceived", "relayMessageContent"],
        ["Item"] = ["id", "ownerDbId", "parentId", "itemName", "comments", "createdAt", "updatedAt"],
        ["LinkedRecord"] = ["id", "ownerDbId", "record1Id", "record2Id", "record1TypeId", "record2TypeId", "createdAt"],
        ["Note"] = ["id", "ownerDbId", "title", "textContents", "createDateTime", "createDate", "createdAt", "updatedAt"],
        ["Organization"] = ["id", "ownerDbId", "organizationName", "comments", "createdAt", "updatedAt"],
        ["Settings"] = ["settingId", "settingName", "valueInt", "valueText"],
        ["SharedRecord"] = ["id", "sharedWithDbId", "shareOwnerDbId", "recordId", "recordTypeId", "explicitShare", "alsoShareLinked"],
    };

    /// <summary>Columns of versions 1 and 2.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Version2RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Category"] = ["id", "ownerDbId", "parentId", "categoryName", "comments"],
        ["Contact"] = ["id", "ownerDbId", "nameLast", "nameFirst", "jobTitle", "company", "phoneMain", "phoneMobile", "phoneHome", "phoneWork", "emailMain", "emailMobile", "emailHome", "emailWork", "comments"],
        ["Database"] = ["id", "name", "type", "requestSentDateTime", "requestReceivedDateTIme", "requestAcceptedDateTine", "keyPrivate", "keyPublic"],
        ["Incoming"] = ["id", "dateReceived", "relayMessageContent"],
        ["Item"] = ["id", "ownerDbId", "parentId", "itemName", "comments"],
        ["LinkedRecord"] = ["id", "ownerDbId", "record1Id", "record2Id", "record1TypeId", "record2TypeId"],
        ["Note"] = ["id", "ownerDbId", "title", "textContents", "createDateTime", "createDate"],
        ["Organization"] = ["id", "ownerDbId", "organizationName", "comments"],
        ["SharedRecord"] = ["id", "sharedWithDbId", "shareOwnerDbId", "recordId", "recordTypeId", "explicitShare", "alsoShareLinked"],
    };
}
