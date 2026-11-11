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

/// <summary>The 92-byte header of an encrypted backup, as read from a file.</summary>
public sealed record EncryptedBackupHeader(
    BackupKdfParameters KdfParameters,
    int ChunkSize,
    byte[] KdfSalt,
    byte[] FileSalt,
    byte[] Bytes);

/// <summary>
/// Encrypted backups, format version 1: the plain backup file wrapped in a header and AES-256-GCM chunks,
/// identical byte for byte with Gnoseis for Android. The file key comes from the master key with
/// HKDF-SHA-512 and a salt new for every file; each chunk's nonce is its index and a last-chunk flag, and
/// the header is authenticated with every chunk. Streams the data; the file is never held in memory whole.
/// </summary>
public static class EncryptedBackupFormat
{
    public const int HeaderSize = 92;
    public const int TagSize = 16;
    public const int NonceSize = 12;
    public const int FileSaltSize = 32;

    /// <summary>The chunk size a writer always uses: 64 KiB of plaintext.</summary>
    public const int WrittenChunkSize = 65536;
    public const int MinChunkSize = 4 * 1024;
    public const int MaxChunkSize = 1024 * 1024;

    public const byte FormatVersion = 1;
    public const byte KdfArgon2id = 1;
    public const byte CipherAes256GcmChunked = 1;

    private static readonly byte[] Magic = "GNOSEISB"u8.ToArray();
    private static readonly byte[] FileKeyInfo = "org.gnoseis.backup/v1/file-key"u8.ToArray();

    /// <summary>Whether a file starting with these bytes (at least 8) is an encrypted backup.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> start) =>
        start.Length >= Magic.Length && start[..Magic.Length].SequenceEqual(Magic);

    /// <summary>Whether the file at <paramref name="path"/> is an encrypted backup, by its first 8 bytes.</summary>
    public static bool IsEncryptedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> start = stackalloc byte[8];
        return stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) == start.Length && IsEncrypted(start);
    }

    /// <summary>
    /// Writes <paramref name="plain"/> (read to its end) encrypted with <paramref name="masterKey"/> to
    /// <paramref name="output"/>. An empty input is refused.
    /// </summary>
    public static void Encrypt(Stream plain, Stream output, BackupMasterKey masterKey)
    {
        var header = WriteHeader(masterKey, RandomNumberGenerator.GetBytes(FileSaltSize));
        var fullChunk = header.ChunkSize;
        var current = new byte[fullChunk];
        var next = new byte[fullChunk];
        var cipher = new byte[fullChunk + TagSize];

        var length = plain.ReadAtLeast(current, fullChunk, throwOnEndOfStream: false);
        if (length == 0)
        {
            throw new InvalidOperationException("An empty backup can't be encrypted.");
        }

        using var aes = CreateCipher(masterKey.Key, header);
        output.Write(header.Bytes);
        Span<byte> nonce = stackalloc byte[NonceSize];
        try
        {
            for (long index = 0; ; index++)
            {
                var nextLength = length == fullChunk ? plain.ReadAtLeast(next, fullChunk, throwOnEndOfStream: false) : 0;
                var last = nextLength == 0;
                WriteNonce(nonce, index, last);
                aes.Encrypt(nonce, current.AsSpan(0, length), cipher.AsSpan(0, length), cipher.AsSpan(length, TagSize), header.Bytes);
                output.Write(cipher, 0, length + TagSize);
                if (last)
                {
                    break;
                }
                (current, next) = (next, current);
                length = nextLength;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(next);
        }
    }

    /// <summary>
    /// Reads and checks the header. Refuses an unknown version, KDF, cipher or reserved value, and settings
    /// outside the reader's limits, before any key derivation.
    /// </summary>
    public static EncryptedBackupHeader ReadHeader(Stream input)
    {
        var bytes = new byte[HeaderSize];
        var read = input.ReadAtLeast(bytes, HeaderSize, throwOnEndOfStream: false);
        if (!IsEncrypted(bytes.AsSpan(0, read)))
        {
            throw new EncryptedBackupException(read < Magic.Length ? EncryptedBackupProblem.Truncated : EncryptedBackupProblem.NotEncrypted);
        }
        if (read > Magic.Length && bytes[8] != FormatVersion)
        {
            throw new EncryptedBackupException(EncryptedBackupProblem.UnknownVersion);
        }
        if (read < HeaderSize)
        {
            throw new EncryptedBackupException(EncryptedBackupProblem.Truncated);
        }
        if (bytes[9] != KdfArgon2id || bytes[10] != CipherAes256GcmChunked || bytes[11] != 0)
        {
            throw new EncryptedBackupException(EncryptedBackupProblem.UnknownVersion);
        }

        var span = bytes.AsSpan();
        var memory = BinaryPrimitives.ReadUInt32BigEndian(span[12..]);
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(span[16..]);
        var parallelism = BinaryPrimitives.ReadUInt32BigEndian(span[20..]);
        var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(span[24..]);
        if (memory > int.MaxValue || iterations > int.MaxValue || parallelism > int.MaxValue
            || chunkSize is < MinChunkSize or > MaxChunkSize)
        {
            throw new EncryptedBackupException(EncryptedBackupProblem.ParametersOutOfRange);
        }
        var parameters = new BackupKdfParameters((int)memory, (int)iterations, (int)parallelism);
        if (!parameters.IsWithinLimits)
        {
            throw new EncryptedBackupException(EncryptedBackupProblem.ParametersOutOfRange);
        }

        return new EncryptedBackupHeader(
            parameters, (int)chunkSize, span[28..60].ToArray(), span[60..92].ToArray(), bytes);
    }

    /// <summary>
    /// Decrypts the chunks that follow <paramref name="header"/> in <paramref name="input"/> to
    /// <paramref name="output"/>. Each chunk is written only after it passes its check, but a refused file
    /// may leave earlier chunks in <paramref name="output"/>: write to a private file and delete it on error.
    /// </summary>
    public static void Decrypt(Stream input, EncryptedBackupHeader header, BackupMasterKey masterKey, Stream output)
    {
        var fullChunk = header.ChunkSize + TagSize;
        var current = new byte[fullChunk];
        var next = new byte[fullChunk];
        var plain = new byte[header.ChunkSize];

        using var aes = CreateCipher(masterKey.Key, header);
        Span<byte> nonce = stackalloc byte[NonceSize];
        try
        {
            var length = input.ReadAtLeast(current, fullChunk, throwOnEndOfStream: false);
            for (long index = 0; ; index++)
            {
                if (length <= TagSize)
                {
                    // No chunk at all, or a last chunk too short to hold a byte and its tag.
                    throw new EncryptedBackupException(EncryptedBackupProblem.Truncated);
                }
                var nextLength = length == fullChunk ? input.ReadAtLeast(next, fullChunk, throwOnEndOfStream: false) : 0;
                var last = nextLength == 0;
                var plainLength = length - TagSize;
                var cipherText = current.AsSpan(0, plainLength);
                var tag = current.AsSpan(plainLength, TagSize);
                if (!TryDecrypt(aes, nonce, index, last, cipherText, tag, plain, header.Bytes))
                {
                    // Tell a file cut at a chunk boundary, or one with whole chunks after its last, from damage.
                    var otherFlagFits = TryDecrypt(aes, nonce, index, !last, cipherText, tag, plain, header.Bytes);
                    CryptographicOperations.ZeroMemory(plain);
                    throw new EncryptedBackupException(
                        !otherFlagFits ? EncryptedBackupProblem.AuthenticationFailed
                        : last ? EncryptedBackupProblem.Truncated
                        : EncryptedBackupProblem.TrailingData);
                }
                output.Write(plain, 0, plainLength);
                if (last)
                {
                    return;
                }
                (current, next) = (next, current);
                length = nextLength;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Decrypts a whole encrypted backup with a master key that fits its header.</summary>
    public static void Decrypt(Stream input, BackupMasterKey masterKey, Stream output) =>
        Decrypt(input, ReadHeader(input), masterKey, output);

    /// <summary>
    /// Decrypts a whole encrypted backup with its passphrase (the normalised passphrase's UTF-8 bytes),
    /// deriving the master key with the file's own KDF salt and settings.
    /// </summary>
    public static void Decrypt(Stream input, ReadOnlySpan<byte> passphraseUtf8, Stream output)
    {
        var header = ReadHeader(input);
        using var masterKey = BackupMasterKey.Derive(passphraseUtf8, header.KdfSalt, header.KdfParameters);
        Decrypt(input, header, masterKey, output);
    }

    private static EncryptedBackupHeader WriteHeader(BackupMasterKey masterKey, byte[] fileSalt)
    {
        var bytes = new byte[HeaderSize];
        var span = bytes.AsSpan();
        Magic.CopyTo(span);
        span[8] = FormatVersion;
        span[9] = KdfArgon2id;
        span[10] = CipherAes256GcmChunked;
        span[11] = 0;
        var parameters = masterKey.Parameters;
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)parameters.MemoryKiB);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)parameters.Iterations);
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], (uint)parameters.Parallelism);
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], WrittenChunkSize);
        masterKey.KdfSalt.CopyTo(span[28..60]);
        fileSalt.CopyTo(span[60..92]);
        return new EncryptedBackupHeader(parameters, WrittenChunkSize, masterKey.KdfSalt.ToArray(), fileSalt, bytes);
    }

    /// <summary>HKDF-SHA-512 from the master key and the file salt.</summary>
    internal static void DeriveFileKey(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> fileSalt, Span<byte> fileKey) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA512, masterKey, fileKey, fileSalt, FileKeyInfo);

    private static AesGcm CreateCipher(ReadOnlySpan<byte> masterKey, EncryptedBackupHeader header)
    {
        Span<byte> fileKey = stackalloc byte[32];
        try
        {
            DeriveFileKey(masterKey, header.FileSalt, fileKey);
            return new AesGcm(fileKey, TagSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    /// <summary>The chunk index as an 11-byte big-endian integer, then 1 for the last chunk or 0.</summary>
    private static void WriteNonce(Span<byte> nonce, long index, bool last)
    {
        nonce.Clear();
        BinaryPrimitives.WriteInt64BigEndian(nonce[3..11], index);
        nonce[11] = last ? (byte)1 : (byte)0;
    }

    private static bool TryDecrypt(AesGcm aes, Span<byte> nonce, long index, bool last,
        ReadOnlySpan<byte> cipherText, ReadOnlySpan<byte> tag, byte[] plain, byte[] associatedData)
    {
        WriteNonce(nonce, index, last);
        try
        {
            aes.Decrypt(nonce, cipherText, tag, plain.AsSpan(0, cipherText.Length), associatedData);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }
}
