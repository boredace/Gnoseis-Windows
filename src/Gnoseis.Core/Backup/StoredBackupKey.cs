/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Gnoseis.Core.Backup;

/// <summary>
/// Protects data so only this user on this device can read it back (DPAPI in the app). Unprotect throws
/// <see cref="CryptographicException"/> when it can't (another PC or account, or damage).
/// </summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}

/// <summary>
/// The backup master key kept on this device, so a backup needs no passphrase: the key, its KDF salt and
/// settings, protected for the current user, in a file in the data folder. The passphrase is never stored.
/// A file that can't be opened here (a portable copy on another PC or account) counts as no stored key and
/// is left in place, so it still works when the copy goes back.
/// </summary>
public sealed class StoredBackupKey(string filePath, IKeyProtector protector)
{
    public const string FileName = "backup-key.dat";

    private const byte FileVersion = 1;
    private const int ContentSize = 1 + 3 * 4 + BackupMasterKey.SaltSize + BackupMasterKey.KeySize;

    public string FilePath { get; } = filePath;

    /// <summary>Whether a key file exists, whether or not it can be opened on this device.</summary>
    public bool FileExists => File.Exists(FilePath);

    /// <summary>The stored key, or null when there is none or it can't be opened here. Dispose it after use.</summary>
    public BackupMasterKey? Load()
    {
        byte[] content;
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            content = protector.Unprotect(File.ReadAllBytes(FilePath));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            if (content.Length != ContentSize || content[0] != FileVersion)
            {
                return null;
            }
            var span = content.AsSpan();
            var parameters = new BackupKdfParameters(
                (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(span[1..]), int.MaxValue),
                (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(span[5..]), int.MaxValue),
                (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(span[9..]), int.MaxValue));
            if (!parameters.IsWithinLimits)
            {
                return null;
            }
            var salt = span.Slice(13, BackupMasterKey.SaltSize);
            var key = span.Slice(13 + BackupMasterKey.SaltSize, BackupMasterKey.KeySize);
            return BackupMasterKey.FromKey(key, salt, parameters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    /// <summary>Stores the key, replacing any stored before (setting up or changing the passphrase).</summary>
    public void Save(BackupMasterKey key)
    {
        var content = new byte[ContentSize];
        try
        {
            var span = content.AsSpan();
            span[0] = FileVersion;
            BinaryPrimitives.WriteUInt32BigEndian(span[1..], (uint)key.Parameters.MemoryKiB);
            BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)key.Parameters.Iterations);
            BinaryPrimitives.WriteUInt32BigEndian(span[9..], (uint)key.Parameters.Parallelism);
            key.KdfSalt.CopyTo(span[13..]);
            key.Key.CopyTo(span[(13 + BackupMasterKey.SaltSize)..]);

            var protectedContent = protector.Protect(content);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var temporaryPath = FilePath + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, protectedContent);
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    /// <summary>Turns encryption off on this device. Encrypted backups already made are not touched.</summary>
    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
