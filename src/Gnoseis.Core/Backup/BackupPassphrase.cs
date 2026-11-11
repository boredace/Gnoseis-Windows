/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using System.Text;

namespace Gnoseis.Core.Backup;

/// <summary>Whether a passphrase the user typed can be set up as the backup passphrase.</summary>
public enum PassphraseProblem
{
    None,

    /// <summary>Fewer than <see cref="BackupPassphrase.MinimumLength"/> code points after normalising.</summary>
    TooShort,

    /// <summary>Contains an unpaired surrogate or a noncharacter.</summary>
    UnusableCharacter,
}

/// <summary>
/// The backup passphrase: normalising it to the bytes the key is derived from (the same on Gnoseis for
/// Android, so a backup opens on either), the rule for one the user chooses, and generated passphrases.
/// </summary>
public static class BackupPassphrase
{
    /// <summary>The fewest code points a passphrase the user chooses may have, after normalising.</summary>
    public const int MinimumLength = 12;

    /// <summary>The number of words in a generated passphrase (about 258 bits).</summary>
    public const int GeneratedWordCount = 20;

    private const string WordListResource = "Gnoseis.Core.Backup.eff_large_wordlist.txt";

    private static readonly Lazy<IReadOnlyList<string>> LazyWords = new(LoadWords);

    /// <summary>The EFF large word list: 7,776 lower-case words.</summary>
    public static IReadOnlyList<string> Words => LazyWords.Value;

    /// <summary>
    /// Normalises a passphrase: refuses unpaired surrogates and noncharacters, applies NFKC, then removes
    /// white space at both ends and turns every run of it inside into one space. White space is a fixed
    /// list of code points, never the platform's own test, so both clients get the same bytes.
    /// Returns false for a passphrase that is refused.
    /// </summary>
    public static bool TryNormalise(string passphrase, out string normalised)
    {
        normalised = "";
        if (!HasOnlyUsableCharacters(passphrase))
        {
            return false;
        }

        var composed = passphrase.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var c in composed)
        {
            // Every white space code point is in the basic range, so testing UTF-16 units is exact.
            if (IsWhiteSpace(c))
            {
                pendingSpace = result.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }
            result.Append(c);
        }
        normalised = result.ToString();
        return true;
    }

    /// <summary>The UTF-8 bytes of the normalised passphrase, or null if it is refused. Clear them after use.</summary>
    public static byte[]? ToKeyBytes(string passphrase) =>
        TryNormalise(passphrase, out var normalised) ? Encoding.UTF8.GetBytes(normalised) : null;

    /// <summary>Whether a passphrase the user chose can be set up (the strength estimate is separate advice).</summary>
    public static PassphraseProblem Check(string passphrase)
    {
        if (!TryNormalise(passphrase, out var normalised))
        {
            return PassphraseProblem.UnusableCharacter;
        }
        return CountCodePoints(normalised) < MinimumLength ? PassphraseProblem.TooShort : PassphraseProblem.None;
    }

    /// <summary>20 words from the EFF large word list, picked with the OS's secure random generator.</summary>
    public static string Generate()
    {
        var words = Words;
        var picked = new string[GeneratedWordCount];
        for (var i = 0; i < picked.Length; i++)
        {
            picked[i] = words[RandomNumberGenerator.GetInt32(words.Count)];
        }
        return string.Join(' ', picked);
    }

    /// <summary>
    /// Unicode's 25 White_Space code points, as a fixed list: the platforms' own tests differ (Kotlin's
    /// leaves out U+0085 and adds U+001C-001F), and Gnoseis for Android uses this same list.
    /// </summary>
    public static bool IsWhiteSpace(char c) => (int)c switch
    {
        >= 0x0009 and <= 0x000D => true,
        0x0020 or 0x0085 or 0x00A0 or 0x1680 => true,
        >= 0x2000 and <= 0x200A => true,
        0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        _ => false,
    };

    internal static int CountCodePoints(string text) => text.EnumerateRunes().Count();

    private static bool HasOnlyUsableCharacters(string text)
    {
        var span = text.AsSpan();
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                return false; // an unpaired surrogate
            }
            var value = rune.Value;
            if (value is >= 0xFDD0 and <= 0xFDEF || (value & 0xFFFE) == 0xFFFE)
            {
                return false; // a noncharacter
            }
            span = span[consumed..];
        }
        return true;
    }

    private static IReadOnlyList<string> LoadWords()
    {
        using var stream = typeof(BackupPassphrase).Assembly.GetManifestResourceStream(WordListResource)
            ?? throw new InvalidOperationException("The word list is missing.");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var words = new List<string>(7776);
        while (reader.ReadLine() is { } line)
        {
            // Each line is the five dice digits, a tab, then the word.
            var tab = line.IndexOf('\t');
            if (tab >= 0)
            {
                words.Add(line[(tab + 1)..]);
            }
        }
        return words;
    }
}
