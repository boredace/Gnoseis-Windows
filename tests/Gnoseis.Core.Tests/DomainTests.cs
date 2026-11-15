/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>Same cases as the Android ValidateEmailTest / ValidatePhoneTest (android.util.Patterns behaviour).</summary>
public class ValidationTests
{
    private const string EmailBlank = "The email can't be blank";
    private const string EmailInvalid = "That's not a valid email";
    private const string PhoneBlank = "The phone number can't be blank";
    private const string PhoneInvalid = "That's not a valid phone number";

    [Theory]
    [InlineData("", false, EmailBlank)]
    [InlineData("   ", false, EmailBlank)]
    [InlineData("\t\n", false, EmailBlank)]
    [InlineData("a@b.co", true, null)]
    [InlineData("john.doe@example.com", true, null)]
    [InlineData("user+tag@example.org", true, null)]
    [InlineData("first_last@sub.domain.com", true, null)]
    [InlineData("UPPER@EXAMPLE.COM", true, null)]
    [InlineData("x%y@ex-ample.com", true, null)]
    [InlineData("1@2.3", true, null)]
    [InlineData("a@b.c", true, null)]
    [InlineData("user.@example.com", true, null)]
    [InlineData("plainaddress", false, EmailInvalid)]
    [InlineData("@example.com", false, EmailInvalid)]
    [InlineData("user@", false, EmailInvalid)]
    [InlineData("user@example", false, EmailInvalid)]
    [InlineData("user@.com", false, EmailInvalid)]
    [InlineData("user@@example.com", false, EmailInvalid)]
    [InlineData("user name@example.com", false, EmailInvalid)]
    [InlineData(" user@example.com", false, EmailInvalid)]
    [InlineData("user@example.com ", false, EmailInvalid)]
    [InlineData("user@-example.com", false, EmailInvalid)]
    [InlineData("user@example..com", false, EmailInvalid)]
    [InlineData("jürgen@example.com", false, EmailInvalid)]
    public void ValidateEmail_matches_Android(string input, bool expectedSuccess, string? expectedMessage)
    {
        var result = ValidateEmail.Execute(input);
        Assert.Equal(expectedSuccess, result.Successful);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("", false, PhoneBlank)]
    [InlineData("   ", false, PhoneBlank)]
    [InlineData("123", true, null)]
    [InlineData("5551234", true, null)]
    [InlineData("555-1234", true, null)]
    [InlineData("555.123.4567", true, null)]
    [InlineData("(555) 123-4567", true, null)]
    [InlineData("+1 555 123 4567", true, null)]
    [InlineData("+44 (20) 7946.0958", true, null)]
    [InlineData("12345678901234567890", true, null)]
    [InlineData("1", false, PhoneInvalid)]
    [InlineData("12", false, PhoneInvalid)]
    [InlineData("abc", false, PhoneInvalid)]
    [InlineData("555-CALL", false, PhoneInvalid)]
    [InlineData("+", false, PhoneInvalid)]
    [InlineData("123-", false, PhoneInvalid)]
    [InlineData("-123", false, PhoneInvalid)]
    [InlineData("()123", false, PhoneInvalid)]
    [InlineData("555 1234 ext. 5", false, PhoneInvalid)]
    public void ValidatePhone_matches_Android(string input, bool expectedSuccess, string? expectedMessage)
    {
        var result = ValidatePhone.Execute(input);
        Assert.Equal(expectedSuccess, result.Successful);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }
}
