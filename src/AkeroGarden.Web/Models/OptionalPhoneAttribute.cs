using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Models;

/// <summary>
/// Phone validation for an <b>optional</b> field. The built-in <see cref="PhoneAttribute"/> rejects
/// an empty string (the SSR form binder posts "" for a blank input, not null), which wrongly blocks
/// submitting a form that leaves an optional phone blank. This treats blank as valid and only
/// applies the phone check to a non-empty value.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly PhoneAttribute Phone = new();

    public override bool IsValid(object? value)
        => value is not string s || string.IsNullOrWhiteSpace(s) || Phone.IsValid(s);
}
