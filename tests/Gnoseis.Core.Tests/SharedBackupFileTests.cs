/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Encrypted backups shared with Gnoseis-Android: encrypted-v1-windows.backup was made by this client
/// (a new database with one note and one contact added) and goes to Android's tests with its passphrase.
/// </summary>
public class SharedBackupFileTests
{
    /// <summary>The passphrase of encrypted-v1-windows.backup. It is the same before and after normalising.</summary>
    public const string WindowsFilePassphrase = "correct horse battery staple windows";

    private static string SharedFile(string name) => Path.Combine(AppContext.BaseDirectory, "SharedBackups", name);

    [Fact]
    public async Task The_Windows_file_decrypts_to_a_valid_backup()
    {
        using var test = new TestDatabase();
        var path = SharedFile("encrypted-v1-windows.backup");
        Assert.True(EncryptedBackupFormat.IsEncryptedFile(path));
        var plain = test.PathFor("decrypted.backup");

        using (var input = File.OpenRead(path))
        using (var output = File.Create(plain))
        {
            EncryptedBackupFormat.Decrypt(input, EncryptedBackupFormatTests.Passphrase(WindowsFilePassphrase), output);
        }

        var inspection = await BackupRepository.InspectAsync(plain);
        Assert.True(inspection.IsValid, inspection.Problem);
        Assert.Equal((2, 1), (inspection.NoteCount, inspection.ContactCount));
    }

    [Fact]
    public void The_Windows_file_has_the_written_settings_and_two_chunks()
    {
        var path = SharedFile("encrypted-v1-windows.backup");
        using var input = File.OpenRead(path);

        var header = EncryptedBackupFormat.ReadHeader(input);

        Assert.Equal(BackupKdfParameters.Written, header.KdfParameters);
        Assert.Equal(EncryptedBackupFormat.WrittenChunkSize, header.ChunkSize);
        Assert.Equal(92 + 86016 + 2 * 16, new FileInfo(path).Length);
    }
}
