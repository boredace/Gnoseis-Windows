/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using Gnoseis.Core.Data;

namespace Gnoseis.Core.Backup;

/// <summary>Whether backups made on this device are encrypted.</summary>
public enum BackupEncryptionState
{
    /// <summary>Backups are plain, as before.</summary>
    Off,

    /// <summary>Backups are encrypted with the stored key.</summary>
    On,

    /// <summary>
    /// Encryption was set up, but the stored key can't be opened here (another Windows user or PC, or the
    /// file is damaged). Back up writes nothing until encryption is set up again; only Back up without
    /// encryption, which the user picks knowingly, writes a plain file.
    /// </summary>
    KeyUnavailable,
}

/// <summary>Back up refused because encryption is on but its key can't be opened on this device.</summary>
public sealed class BackupKeyUnavailableException()
    : Exception("Encryption was set up for another Windows user or PC. Set it up again to encrypt backups here.");

/// <summary>
/// What Back up does with encryption on or off, setting up and turning off encryption, and the keys a
/// restore uses. The UI asks; this decides.
/// </summary>
public sealed class BackupEncryption(BackupRepository backups, StoredBackupKey storedKey)
{
    public const string EncryptedFileNameSuffix = ".encrypted";

    public BackupEncryptionState State
    {
        get
        {
            if (!storedKey.FileExists)
            {
                return BackupEncryptionState.Off;
            }
            using var key = storedKey.Load();
            return key == null ? BackupEncryptionState.KeyUnavailable : BackupEncryptionState.On;
        }
    }

    /// <summary>The name the save dialog suggests, without the .backup extension the dialog adds.</summary>
    public static string SuggestedFileName(DateTime now, bool encrypted) =>
        BackupRepository.DefaultBackupFileName(now) + (encrypted ? EncryptedFileNameSuffix : "");

    /// <summary>
    /// Back up (Settings and Home's Back up now): encrypted while encryption is on, plain while it is off.
    /// Returns whether the file is encrypted.
    /// </summary>
    /// <exception cref="BackupKeyUnavailableException">Encryption is on but its key can't be opened here.</exception>
    public async Task<bool> BackUpAsync(string path)
    {
        if (!storedKey.FileExists)
        {
            await backups.ExportAsync(path).ConfigureAwait(false);
            return false;
        }
        using var key = storedKey.Load() ?? throw new BackupKeyUnavailableException();
        await backups.ExportAsync(path, key).ConfigureAwait(false);
        return true;
    }

    /// <summary>Settings' "Back up without encryption": always a plain file.</summary>
    public Task BackUpWithoutEncryptionAsync(string path) => backups.ExportAsync(path);

    /// <summary>
    /// Sets up (or changes) the backup passphrase: a new KDF salt and master key, stored on this device.
    /// Takes about a second. Returns false if the passphrase can't be used (see <see cref="BackupPassphrase.Check"/>).
    /// </summary>
    public Task<bool> SetPassphraseAsync(string passphrase) => Task.Run(() =>
    {
        var bytes = BackupPassphrase.ToKeyBytes(passphrase);
        if (bytes == null)
        {
            return false;
        }
        try
        {
            using var key = BackupMasterKey.Create(bytes);
            storedKey.Save(key);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    });

    /// <summary>After a restore with a typed passphrase: keep encrypting backups here with that key.</summary>
    public void KeepKey(BackupMasterKey key) => storedKey.Save(key);

    /// <summary>Turns encryption off: the stored key is deleted. Encrypted backups already made are not touched.</summary>
    public void TurnOff() => storedKey.Delete();

    /// <summary>A copy of the stored key if it fits a file with this header, otherwise null.</summary>
    public BackupMasterKey? StoredKeyFor(EncryptedBackupHeader header)
    {
        var key = storedKey.Load();
        if (key != null && key.Fits(header))
        {
            return key;
        }
        key?.Dispose();
        return null;
    }

    /// <summary>
    /// Derives the key for <paramref name="path"/> from a typed passphrase and checks it on the file's first
    /// chunk. Null when the passphrase is wrong, can't be used, or the file is damaged. Takes about a second.
    /// </summary>
    public static Task<BackupMasterKey?> TryPassphraseAsync(string path, EncryptedBackupHeader header, string passphrase) => Task.Run(() =>
    {
        var bytes = BackupPassphrase.ToKeyBytes(passphrase);
        if (bytes == null)
        {
            return null;
        }
        BackupMasterKey? key = null;
        try
        {
            key = BackupMasterKey.Derive(bytes, header.KdfSalt, header.KdfParameters);
            if (EncryptedBackupFormat.KeyOpens(path, key))
            {
                var opened = key;
                key = null;
                return opened;
            }
            return null;
        }
        finally
        {
            key?.Dispose();
            CryptographicOperations.ZeroMemory(bytes);
        }
    });
}
