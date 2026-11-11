/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Security.Cryptography;
using System.Text;
using Gnoseis.Core.Backup;

namespace Gnoseis.Core.Tests;

/// <summary>Normalising, the length rule and generated passphrases (the same rules as Gnoseis for Android).</summary>
public class BackupPassphraseTests
{
    private static readonly int[] WhiteSpaceCodePoints =
    [
        0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020, 0x0085, 0x00A0, 0x1680,
        0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
        0x2028, 0x2029, 0x202F, 0x205F, 0x3000,
    ];

    private static string Normalised(string passphrase)
    {
        Assert.True(BackupPassphrase.TryNormalise(passphrase, out var normalised));
        return normalised;
    }

    // ---- Normalising

    [Fact]
    public void Composed_and_decomposed_forms_give_the_same_passphrase()
    {
        Assert.Equal(Normalised("caf\u00E9 cr\u00E8me"), Normalised("cafe\u0301 cre\u0300me"));
        Assert.Equal("caf\u00E9", Normalised("cafe\u0301"));
    }

    [Fact]
    public void Full_width_characters_become_their_usual_forms()
    {
        Assert.Equal("ABC123 xyz", Normalised("\uFF21\uFF22\uFF23\uFF11\uFF12\uFF13\u3000\uFF58\uFF59\uFF5A"));
    }

    [Theory]
    [InlineData("  correct horse  ", "correct horse")]
    [InlineData("correct  horse", "correct horse")]
    [InlineData("correct\r\nhorse\nbattery", "correct horse battery")]
    [InlineData("correct\t \n horse", "correct horse")]
    [InlineData("\n\ncorrect horse\n", "correct horse")]
    [InlineData("Correct Horse", "Correct Horse")]
    [InlineData("   ", "")]
    public void White_space_is_trimmed_and_collapsed_and_case_is_kept(string typed, string expected)
    {
        Assert.Equal(expected, Normalised(typed));
    }

    [Theory]
    [InlineData('\u0085')]
    [InlineData('\u1680')]
    [InlineData('\u2028')]
    [InlineData('\u00A0')]
    public void Unusual_white_space_inside_counts_as_a_space(char space)
    {
        Assert.Equal("ab cd", Normalised($"ab{space}cd"));
        Assert.Equal("ab cd", Normalised($"{space}ab{space}{space}cd{space}"));
    }

    [Fact]
    public void Exactly_the_25_white_space_code_points_count_as_white_space()
    {
        for (var c = 0; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }
            Assert.Equal(WhiteSpaceCodePoints.Contains(c), BackupPassphrase.IsWhiteSpace((char)c));
        }
    }

    [Theory]
    [InlineData('\u001F')]
    [InlineData('\u001C')]
    [InlineData('\u200B')]
    [InlineData('\u200C')]
    [InlineData('\u200D')]
    [InlineData('\uFEFF')]
    [InlineData('\u180E')]
    public void Other_characters_are_kept(char kept)
    {
        Assert.Equal($"ab{kept}cd", Normalised($"ab{kept}cd"));
        Assert.Equal($"{kept}ab", Normalised($"{kept}ab"));
    }

    [Fact]
    public void A_spacing_accent_turns_into_a_space_that_is_then_trimmed()
    {
        Assert.Equal("\u0301abc", Normalised("\u00B4abc"));
    }

    [Theory]
    [InlineData("abc\uFFFE")]
    [InlineData("abc\uFFFF")]
    [InlineData("abc\uFDD0")]
    [InlineData("abc\uFDEF")]
    [InlineData("abc\U0001FFFF")] // U+1FFFF
    [InlineData("abc\U0010FFFE")] // U+10FFFE
    public void Noncharacters_are_refused(string passphrase)
    {
        Assert.False(BackupPassphrase.TryNormalise(passphrase, out _));
        Assert.Null(BackupPassphrase.ToKeyBytes(passphrase));
        Assert.Equal(PassphraseProblem.UnusableCharacter, BackupPassphrase.Check(passphrase + " long enough now"));
    }

    [Fact]
    public void Unpaired_surrogates_are_refused()
    {
        // Built in code: xUnit replaces a lone surrogate in InlineData with U+FFFD.
        string[] passphrases =
        [
            "abc" + (char)0xD800 + "def",
            "abc" + (char)0xDC00,
            ((char)0xD83D).ToString(),
            "abc" + (char)0xDC00 + (char)0xD800 + "def", // a low surrogate before a high one
        ];
        foreach (var passphrase in passphrases)
        {
            Assert.False(BackupPassphrase.TryNormalise(passphrase, out _));
            Assert.Null(BackupPassphrase.ToKeyBytes(passphrase));
            Assert.Equal(PassphraseProblem.UnusableCharacter, BackupPassphrase.Check(passphrase + " long enough now"));
        }
    }

    [Fact]
    public void Characters_outside_the_basic_range_are_kept()
    {
        Assert.Equal("abc \U0001F600", Normalised("abc  \U0001F600 "));
    }

    [Fact]
    public void Key_bytes_are_the_utf8_of_the_normalised_passphrase()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("caf\u00E9 horse"), BackupPassphrase.ToKeyBytes("  cafe\u0301\n\nhorse "));
    }

    // ---- Passphrase the user chooses

    [Fact]
    public void The_minimum_counts_code_points_after_normalising()
    {
        Assert.Equal(PassphraseProblem.None, BackupPassphrase.Check("abcdefghijk\U0001F600")); // 12 code points
        Assert.Equal(PassphraseProblem.TooShort, BackupPassphrase.Check("abcdefghijk")); // 11
        Assert.Equal(PassphraseProblem.TooShort, BackupPassphrase.Check("\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600")); // 12 UTF-16 units, 6 code points
        Assert.Equal(PassphraseProblem.TooShort, BackupPassphrase.Check("  abcde   fghij  ")); // "abcde fghij" is 11
        Assert.Equal(PassphraseProblem.None, BackupPassphrase.Check("abcdefghijkl"));
    }

    // ---- Generated passphrase

    [Fact]
    public void The_word_list_is_the_eff_large_word_list_unchanged()
    {
        using var stream = typeof(BackupPassphrase).Assembly.GetManifestResourceStream("Gnoseis.Core.Backup.eff_large_wordlist.txt");
        Assert.NotNull(stream);

        Assert.Equal("addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e",
            Convert.ToHexStringLower(SHA256.HashData(stream)));
        Assert.Equal(7776, BackupPassphrase.Words.Count);
        Assert.Equal(7776, BackupPassphrase.Words.Distinct().Count());
        Assert.Equal("abacus", BackupPassphrase.Words[0]);
        Assert.Equal("zoom", BackupPassphrase.Words[^1]);
    }

    [Fact]
    public void A_generated_passphrase_has_20_words_from_the_list()
    {
        var words = BackupPassphrase.Words.ToHashSet();

        var generated = BackupPassphrase.Generate().Split(' ');

        Assert.Equal(20, generated.Length);
        Assert.All(generated, word => Assert.Contains(word, words));
    }

    [Fact]
    public void A_generated_passphrase_is_already_normalised_and_long_enough()
    {
        var generated = BackupPassphrase.Generate();

        Assert.Equal(generated, Normalised(generated));
        Assert.Equal(PassphraseProblem.None, BackupPassphrase.Check(generated));
    }

    [Fact]
    public void A_generated_passphrase_can_be_typed_back_with_line_breaks_and_double_spaces()
    {
        var generated = BackupPassphrase.Generate();
        var words = generated.Split(' ');
        var typed = "  " + string.Join("  ", words[..4]) + "\r\n" + string.Join("\n", words[4..]) + "\n";

        Assert.Equal(BackupPassphrase.ToKeyBytes(generated), BackupPassphrase.ToKeyBytes(typed));
    }

    [Fact]
    public void Generated_passphrases_differ()
    {
        Assert.NotEqual(BackupPassphrase.Generate(), BackupPassphrase.Generate());
    }
}
