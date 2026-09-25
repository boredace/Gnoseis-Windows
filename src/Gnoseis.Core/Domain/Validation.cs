/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Text.RegularExpressions;

namespace Gnoseis.Core.Domain;

public sealed record ValidationResult(bool Successful, string? ErrorMessage = null)
{
    public static ValidationResult Success { get; } = new(true);
}

/// <summary>E-mail check using the same pattern as android.util.Patterns.EMAIL_ADDRESS.</summary>
public static partial class ValidateEmail
{
    [GeneratedRegex(@"^[a-zA-Z0-9+._%\-]{1,256}@[a-zA-Z0-9][a-zA-Z0-9\-]{0,64}(\.[a-zA-Z0-9][a-zA-Z0-9\-]{0,25})+\z")]
    private static partial Regex EmailPattern();

    public static ValidationResult Execute(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new ValidationResult(false, "The email can't be blank");
        }
        if (!EmailPattern().IsMatch(email))
        {
            return new ValidationResult(false, "That's not a valid email");
        }
        return ValidationResult.Success;
    }
}

/// <summary>Phone number check using the same pattern as android.util.Patterns.PHONE.</summary>
public static partial class ValidatePhone
{
    [GeneratedRegex(@"^(\+[0-9]+[\- \.]*)?(\([0-9]+\)[\- \.]*)?([0-9][0-9\- \.]+[0-9])\z")]
    private static partial Regex PhonePattern();

    public static ValidationResult Execute(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return new ValidationResult(false, "The phone number can't be blank");
        }
        if (!PhonePattern().IsMatch(phone))
        {
            return new ValidationResult(false, "That's not a valid phone number");
        }
        return ValidationResult.Success;
    }
}
