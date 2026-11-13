/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gnoseis.Core.Backup;

namespace Gnoseis.Core.Tests;

/// <summary>
/// Test vectors shared byte for byte with Gnoseis-Android, which holds the originals and runs the same files
/// through its own code: backup-key-vectors-v1.json (normalising, Argon2id and HKDF together) and
/// passphrase-normalising-v1.json (normalising alone, written by hand from design doc 08 section 5.1).
/// </summary>
public class SharedBackupVectorsTests
{
    private const string KeyVectors = "backup-key-vectors-v1.json";
    private const string Normalising = "passphrase-normalising-v1.json";
    private const string KeyVectorsSha256 = "0c25c32770876e1a1fa42d71cbd5cd4a90a1c6b9f6787a7ec1d956be9cd3787d";
    private const string NormalisingSha256 = "7bee1e4ad9fca064e19a0186ddd331d83b5c9953873cdd072c7b5a5a43eebee9";

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SharedBackups", name));

    [Theory]
    [InlineData(KeyVectors, KeyVectorsSha256)]
    [InlineData(Normalising, NormalisingSha256)]
    public void The_copy_is_byte_for_byte_Androids(string name, string sha256) =>
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Read(name))));

    [Fact]
    public void The_key_vectors_give_the_same_keys()
    {
        using var document = JsonDocument.Parse(Read(KeyVectors));
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToList();
        Assert.Equal(3, vectors.Count);

        foreach (var vector in vectors)
        {
            var name = vector.GetProperty("name").GetString();
            var bytes = BackupPassphrase.ToKeyBytes(JsonString(vector.GetProperty("passphrase")));
            Assert.NotNull(bytes);
            Assert.True(vector.GetProperty("normalisedHex").GetString() == Convert.ToHexStringLower(bytes), $"{name}: normalised");

            var parameters = new BackupKdfParameters(
                vector.GetProperty("memoryKiB").GetInt32(),
                vector.GetProperty("iterations").GetInt32(),
                vector.GetProperty("parallelism").GetInt32());
            using var master = BackupMasterKey.Derive(bytes, Hex(vector, "kdfSaltHex"), parameters);
            Assert.True(vector.GetProperty("masterKeyHex").GetString() == Convert.ToHexStringLower(master.Key), $"{name}: master key");

            var fileKey = new byte[32];
            EncryptedBackupFormat.DeriveFileKey(master.Key, Hex(vector, "fileSaltHex"), fileKey);
            Assert.True(vector.GetProperty("fileKeyHex").GetString() == Convert.ToHexStringLower(fileKey), $"{name}: file key");
        }
    }

    [Fact]
    public void The_normalising_list_gives_the_expected_bytes()
    {
        using var document = JsonDocument.Parse(Read(Normalising));
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.True(entries.Count >= 30);

        var failures = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var input = JsonString(entries[i].GetProperty("input"));
            var label = $"entry {i}: {string.Join(' ', input.Select(c => $"U+{(int)c:X4}"))}";

            var expected = entries[i].GetProperty("expectedHex").GetString();
            var bytes = BackupPassphrase.ToKeyBytes(input);
            var actual = bytes is null ? "refused" : Convert.ToHexStringLower(bytes);
            if (expected != actual)
            {
                failures.Add($"{label}: expected {expected}, got {actual}");
            }

            var meetsMinimum = entries[i].GetProperty("meetsMinimum").GetBoolean();
            if (meetsMinimum != (BackupPassphrase.Check(input) == PassphraseProblem.None))
            {
                failures.Add($"{label}: meetsMinimum should be {meetsMinimum}");
            }
        }
        Assert.Empty(failures);
    }

    private static byte[] Hex(JsonElement vector, string property) => Convert.FromHexString(vector.GetProperty(property).GetString()!);

    /// <summary>
    /// A JSON string's value, unescaped here rather than by System.Text.Json, which refuses an escaped unpaired
    /// surrogate such as \uD800; the normalising list needs those to reach the passphrase code as they are.
    /// </summary>
    private static string JsonString(JsonElement element)
    {
        var raw = element.GetRawText();
        Assert.True(raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"');
        var text = new StringBuilder();
        for (var i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] != '\\')
            {
                text.Append(raw[i]);
                continue;
            }
            var escape = raw[++i];
            switch (escape)
            {
                case 'u':
                    text.Append((char)int.Parse(raw.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                default: text.Append(escape); break; // \" \\ \/
            }
        }
        return text.ToString();
    }
}
