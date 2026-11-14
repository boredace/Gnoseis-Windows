/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Gnoseis.Core.Backup;

/// <summary>
/// The 256-bit key derived from a backup passphrase with Argon2id, with the KDF salt and settings it was
/// derived with. Disposing it clears the key.
/// </summary>
public sealed class BackupMasterKey : IDisposable
{
    public const int KeySize = 32;
    public const int SaltSize = 32;

    private readonly byte[] _key;
    private readonly byte[] _kdfSalt;
    private bool _disposed;

    private BackupMasterKey(byte[] key, byte[] kdfSalt, BackupKdfParameters parameters)
    {
        _key = key;
        _kdfSalt = kdfSalt;
        Parameters = parameters;
    }

    public BackupKdfParameters Parameters { get; }

    public ReadOnlySpan<byte> KdfSalt => _kdfSalt;

    /// <summary>The key itself, for encrypting files and for storing it protected on this device.</summary>
    public ReadOnlySpan<byte> Key
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <summary>A new passphrase setup: a new random KDF salt and the settings a writer always uses.</summary>
    public static BackupMasterKey Create(ReadOnlySpan<byte> passphraseUtf8) =>
        Derive(passphraseUtf8, RandomNumberGenerator.GetBytes(SaltSize), BackupKdfParameters.Written);

    /// <summary>Derives the key with Argon2id (version 1.3) from the normalised passphrase's UTF-8 bytes.</summary>
    public static BackupMasterKey Derive(ReadOnlySpan<byte> passphraseUtf8, ReadOnlySpan<byte> kdfSalt, BackupKdfParameters parameters)
    {
        if (kdfSalt.Length != SaltSize)
        {
            throw new ArgumentException($"The KDF salt must be {SaltSize} bytes.", nameof(kdfSalt));
        }
        if (!parameters.IsWithinLimits)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters));
        }

        var argon2 = new Argon2BytesGenerator();
        argon2.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithMemoryAsKB(parameters.MemoryKiB)
            .WithIterations(parameters.Iterations)
            .WithParallelism(parameters.Parallelism)
            .WithSalt(kdfSalt.ToArray())
            .Build());

        var password = passphraseUtf8.ToArray();
        var key = new byte[KeySize];
        try
        {
            argon2.GenerateBytes(password, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
        return new BackupMasterKey(key, kdfSalt.ToArray(), parameters);
    }

    /// <summary>A key kept on this device (or a test key). The bytes are copied.</summary>
    public static BackupMasterKey FromKey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> kdfSalt, BackupKdfParameters parameters)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"The key must be {KeySize} bytes.", nameof(key));
        }
        if (kdfSalt.Length != SaltSize)
        {
            throw new ArgumentException($"The KDF salt must be {SaltSize} bytes.", nameof(kdfSalt));
        }
        return new BackupMasterKey(key.ToArray(), kdfSalt.ToArray(), parameters);
    }

    /// <summary>Whether this key opens a file with this header without asking for its passphrase.</summary>
    public bool Fits(EncryptedBackupHeader header) =>
        header.KdfParameters == Parameters && header.KdfSalt.AsSpan().SequenceEqual(_kdfSalt);

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }
}
