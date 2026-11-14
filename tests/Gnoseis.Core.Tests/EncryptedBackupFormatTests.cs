/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Gnoseis.Core.Backup;
using Gnoseis.Core.Data;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The encrypted backup container, format version 1.</summary>
public class EncryptedBackupFormatTests
{
    private const int Chunk = EncryptedBackupFormat.WrittenChunkSize;
    private const int StoredChunk = Chunk + EncryptedBackupFormat.TagSize;
    private const int Header = EncryptedBackupFormat.HeaderSize;

    private static BackupMasterKey TestKey() =>
        BackupMasterKey.FromKey(Filled(32, 0x11), Filled(32, 0x22), BackupKdfParameters.Written);

    // ---- Known answers

    [Fact]
    public void Hkdf_sha512_matches_a_known_answer()
    {
        // RFC 5869 test case 1's inputs with SHA-512; the expected output was computed separately with
        // Python's hmac and hashlib.
        var output = HKDF.DeriveKey(HashAlgorithmName.SHA512, Filled(22, 0x0b), 42,
            Enumerable.Range(0x00, 13).Select(i => (byte)i).ToArray(),
            Enumerable.Range(0xf0, 10).Select(i => (byte)i).ToArray());

        Assert.Equal("832390086cda71fb47625bb5ceb168e4c8e26a1a16ed34d9fc7fe92c1481579338da362cb8d9f925d7cb",
            Convert.ToHexStringLower(output));
    }

    [Fact]
    public void File_key_is_hkdf_sha512_with_the_file_salt_and_the_format_info()
    {
        var fileKey = new byte[32];
        EncryptedBackupFormat.DeriveFileKey(Filled(32, 0x11), Filled(32, 0x22), fileKey);

        Assert.Equal("447d0f9a7102a3e6084edefd8233157ff97be6043061447fbbcc0fb812f225d7",
            Convert.ToHexStringLower(fileKey));
    }

    // ---- Writing

    [Theory]
    [InlineData(1)]
    [InlineData(Chunk)]
    [InlineData(Chunk + 1)]
    [InlineData(2 * Chunk)]
    public void Round_trip_keeps_every_byte_and_has_the_documented_size(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        using var key = TestKey();

        var file = Encrypt(plain, key);

        var chunks = (size + Chunk - 1) / Chunk;
        Assert.Equal(Header + size + 16 * chunks, file.Length);
        Assert.Equal(plain, Decrypt(file, key));
    }

    [Fact]
    public void Header_holds_the_written_values()
    {
        using var key = TestKey();
        var file = Encrypt([1, 2, 3], key);

        Assert.Equal("GNOSEISB"u8.ToArray(), file[..8]);
        Assert.Equal(new byte[] { 1, 1, 1, 0 }, file[8..12]);
        Assert.Equal(65536u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(12)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(16)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(20)));
        Assert.Equal(65536u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(24)));
        Assert.Equal(Filled(32, 0x22), file[28..60]);
        Assert.True(EncryptedBackupFormat.IsEncrypted(file));
    }

    [Fact]
    public void Every_file_gets_its_own_file_salt()
    {
        using var key = TestKey();
        var plain = new byte[100];

        var first = Encrypt(plain, key);
        var second = Encrypt(plain, key);

        Assert.NotEqual(first[60..92], second[60..92]);
        Assert.NotEqual(first[Header..], second[Header..]);
    }

    [Fact]
    public void An_empty_backup_is_not_written()
    {
        using var key = TestKey();
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => EncryptedBackupFormat.Encrypt(new MemoryStream(), output, key));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_plain_backup_is_not_taken_for_an_encrypted_one()
    {
        Assert.False(EncryptedBackupFormat.IsEncrypted("SQLite format 3\0"u8));
        Assert.Equal(EncryptedBackupProblem.NotEncrypted,
            Refused(Encoding.ASCII.GetBytes("SQLite format 3\0" + new string('x', 200)), TestKey()));
    }

    [Fact]
    public async Task A_real_backup_round_trips_with_a_passphrase()
    {
        using var test = new TestDatabase();
        var data = test.Open();
        await data.Contacts.AddContactAsync(new Contact { NameLast = "Smith" });
        var backup = test.PathFor("plain.backup");
        await data.Backup.ExportAsync(backup);
        var plain = await File.ReadAllBytesAsync(backup);

        using var key = BackupMasterKey.Create(Passphrase("a passphrase for this test"));
        var file = Encrypt(plain, key);

        using var output = new MemoryStream();
        EncryptedBackupFormat.Decrypt(new MemoryStream(file), Passphrase("a passphrase for this test"), output);
        Assert.Equal(plain, output.ToArray());
    }

    // ---- Keys

    [Fact]
    public void Passphrase_key_is_argon2id_with_the_kdf_salt_and_settings()
    {
        using var first = BackupMasterKey.Derive(Passphrase("same passphrase"), Filled(32, 7), BackupKdfParameters.Written);
        using var second = BackupMasterKey.Derive(Passphrase("same passphrase"), Filled(32, 7), BackupKdfParameters.Written);
        using var otherSalt = BackupMasterKey.Derive(Passphrase("same passphrase"), Filled(32, 8), BackupKdfParameters.Written);

        Assert.Equal(first.Key.ToArray(), second.Key.ToArray());
        Assert.NotEqual(first.Key.ToArray(), otherSalt.Key.ToArray());
    }

    [Fact]
    public void Stored_key_fits_only_files_with_its_kdf_salt_and_settings()
    {
        using var key = TestKey();
        var header = EncryptedBackupFormat.ReadHeader(new MemoryStream(Encrypt([1], key)));
        using var otherSalt = BackupMasterKey.FromKey(Filled(32, 0x11), Filled(32, 0x23), BackupKdfParameters.Written);
        using var otherSettings = BackupMasterKey.FromKey(Filled(32, 0x11), Filled(32, 0x22), BackupKdfParameters.Written with { Iterations = 4 });

        Assert.True(key.Fits(header));
        Assert.False(otherSalt.Fits(header));
        Assert.False(otherSettings.Fits(header));
    }

    [Fact]
    public void A_disposed_key_is_cleared_and_no_longer_usable()
    {
        var key = TestKey();
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.Key.Length);
    }

    // ---- Refusals

    [Fact]
    public void Wrong_passphrase_is_refused()
    {
        using var key = BackupMasterKey.Create(Passphrase("the right passphrase"));
        var file = Encrypt([1, 2, 3], key);

        var e = Assert.Throws<EncryptedBackupException>(() =>
            EncryptedBackupFormat.Decrypt(new MemoryStream(file), Passphrase("the wrong passphrase"), new MemoryStream()));

        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, e.Problem);
        Assert.Equal("Wrong passphrase, or the file is damaged", e.Message);
    }

    [Theory]
    [InlineData(29)] // KDF salt
    [InlineData(70)] // file salt
    [InlineData(Header + 5)] // first chunk
    [InlineData(Header + StoredChunk - 1)] // first chunk's tag
    [InlineData(Header + StoredChunk + 10)] // second chunk
    [InlineData(Header + 2 * StoredChunk + 3)] // last chunk's tag
    public void A_flipped_byte_is_refused(int offset)
    {
        using var key = TestKey();
        var file = Encrypt(new byte[2 * Chunk + 3], key);

        file[offset] ^= 0x01;

        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, Refused(file, key));
    }

    [Fact]
    public void A_file_cut_at_a_chunk_boundary_is_refused()
    {
        using var key = TestKey();
        var file = Encrypt(new byte[2 * Chunk], key);

        Assert.Equal(EncryptedBackupProblem.Truncated, Refused(file[..(Header + StoredChunk)], key));
    }

    [Fact]
    public void A_file_cut_inside_a_chunk_is_refused()
    {
        using var key = TestKey();
        var file = Encrypt(new byte[2 * Chunk], key);

        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, Refused(file[..(Header + StoredChunk + 1000)], key));
        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, Refused(file[..(Header + 1000)], key));
    }

    [Fact]
    public void A_file_without_chunks_or_with_a_cut_header_is_refused()
    {
        using var key = TestKey();
        var file = Encrypt([1], key);

        Assert.Equal(EncryptedBackupProblem.Truncated, Refused(file[..Header], key));
        Assert.Equal(EncryptedBackupProblem.Truncated, Refused(file[..(Header + 16)], key));
        Assert.Equal(EncryptedBackupProblem.Truncated, Refused(file[..50], key));
        Assert.Equal(EncryptedBackupProblem.Truncated, Refused(file[..4], key));
    }

    [Fact]
    public void Extra_data_after_the_last_chunk_is_refused()
    {
        using var key = TestKey();
        var whole = Encrypt(new byte[Chunk], key);
        var small = Encrypt([1, 2, 3], key);

        Assert.Equal(EncryptedBackupProblem.TrailingData, Refused([.. whole, .. whole[Header..]], key));
        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, Refused([.. small, 0], key));
        Assert.Equal(EncryptedBackupProblem.TrailingData, Refused([.. whole, 0], key));
    }

    [Fact]
    public void Swapped_chunks_are_refused()
    {
        using var key = TestKey();
        var plain = new byte[2 * Chunk + 3];
        plain.AsSpan(0, Chunk).Fill(1);
        plain.AsSpan(Chunk, Chunk).Fill(2);
        var file = Encrypt(plain, key);

        var first = file[Header..(Header + StoredChunk)];
        var second = file[(Header + StoredChunk)..(Header + 2 * StoredChunk)];
        second.CopyTo(file, Header);
        first.CopyTo(file, Header + StoredChunk);

        Assert.Equal(EncryptedBackupProblem.AuthenticationFailed, Refused(file, key));
    }

    [Theory]
    [InlineData(12, 8 * 1024 - 1)]
    [InlineData(12, 1024 * 1024 + 1)]
    [InlineData(12, uint.MaxValue)]
    [InlineData(16, 0)]
    [InlineData(16, 17)]
    [InlineData(20, 0)]
    [InlineData(20, 5)]
    [InlineData(24, 4 * 1024 - 1)]
    [InlineData(24, 1024 * 1024 + 1)]
    public void Settings_out_of_range_are_refused_before_any_key_derivation(int offset, uint value)
    {
        using var key = TestKey();
        var file = Encrypt([1, 2, 3], key);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(offset), value);

        // With the passphrase, so a key derivation with these settings would be attempted if not refused first.
        var e = Assert.Throws<EncryptedBackupException>(() =>
            EncryptedBackupFormat.Decrypt(new MemoryStream(file), Passphrase("any passphrase"), new MemoryStream()));

        Assert.Equal(EncryptedBackupProblem.ParametersOutOfRange, e.Problem);
    }

    [Theory]
    [InlineData(12, 8 * 1024)]
    [InlineData(12, 1024 * 1024)]
    [InlineData(16, 16)]
    [InlineData(20, 4)]
    [InlineData(24, 4 * 1024)]
    [InlineData(24, 1024 * 1024)]
    public void Settings_at_the_limits_are_read(int offset, uint value)
    {
        using var key = TestKey();
        var file = Encrypt([1, 2, 3], key);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(offset), value);

        EncryptedBackupFormat.ReadHeader(new MemoryStream(file));
    }

    [Theory]
    [InlineData(8, 2)] // format version
    [InlineData(8, 0)]
    [InlineData(9, 2)] // KDF
    [InlineData(10, 2)] // cipher
    [InlineData(11, 1)] // reserved
    public void An_unknown_version_is_refused(int offset, byte value)
    {
        using var key = TestKey();
        var file = Encrypt([1, 2, 3], key);
        file[offset] = value;

        var e = Assert.Throws<EncryptedBackupException>(() => Decrypt(file, key));

        Assert.Equal(EncryptedBackupProblem.UnknownVersion, e.Problem);
        Assert.Equal("Made by a newer version of Gnoseis", e.Message);
    }

    [Fact]
    public void A_newer_version_with_a_shorter_header_is_still_reported_as_newer()
    {
        using var key = TestKey();
        var file = Encrypt([1, 2, 3], key)[..20];
        file[8] = 2;

        Assert.Equal(EncryptedBackupProblem.UnknownVersion, Refused(file, key));
    }

    // ---- Helpers

    internal static byte[] Passphrase(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Encrypt(byte[] plain, BackupMasterKey key)
    {
        using var output = new MemoryStream();
        EncryptedBackupFormat.Encrypt(new MemoryStream(plain), output, key);
        return output.ToArray();
    }

    private static byte[] Decrypt(byte[] file, BackupMasterKey key)
    {
        using var output = new MemoryStream();
        EncryptedBackupFormat.Decrypt(new MemoryStream(file), key, output);
        return output.ToArray();
    }

    private static EncryptedBackupProblem Refused(byte[] file, BackupMasterKey key) =>
        Assert.Throws<EncryptedBackupException>(() => Decrypt(file, key)).Problem;

    private static byte[] Filled(int length, byte value) => Enumerable.Repeat(value, length).ToArray();
}
