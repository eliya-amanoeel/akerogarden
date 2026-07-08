using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace AkeroGarden.Web.Models;

/// <summary>
/// Phone validation for an <b>optional</b> field. Blank is valid (the SSR form binder posts "" for a
/// blank input, not null, and the built-in <see cref="PhoneAttribute"/> wrongly rejects ""). A
/// non-empty value must look like a real phone number.
///
/// <para><see cref="PhoneAttribute"/> is too lenient for a non-empty value — it accepts letters, so
/// "abc" passes (Phase 7 finding). This applies a stricter rule: only a leading <c>+</c>, digits,
/// spaces, hyphens, parentheses, dots and slashes are allowed, and there must be 6–15 actual digits
/// (E.164 caps national numbers at 15). Letters and too-short/too-long values are rejected.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed partial class OptionalPhoneAttribute : ValidationAttribute
{
    // Permitted characters across the whole value (a single optional leading + then digits/separators).
    [GeneratedRegex(@"^\+?[\d\s\-\(\)\./]+$")]
    private static partial Regex AllowedShape();

    // Everything that is not a digit — stripped to count the actual digits.
    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();

    public override bool IsValid(object? value)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return true; // optional: blank is fine

        if (!AllowedShape().IsMatch(s))
            return false; // contains letters or other disallowed characters

        var digitCount = NonDigits().Replace(s, string.Empty).Length;
        return digitCount is >= 6 and <= 15;
    }
}
