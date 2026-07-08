using AkeroGarden.Web.Models;

namespace AkeroGarden.Unit;

public class OptionalPhoneAttributeTests
{
    private static bool IsValid(string? value) => new OptionalPhoneAttribute().IsValid(value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_is_valid_because_the_field_is_optional(string? value)
        => Assert.True(IsValid(value));

    [Theory]
    [InlineData("0701234567")]
    [InlineData("+46 70 123 45 67")]
    [InlineData("08-123 456")]
    [InlineData("(08) 123 45 67")]
    [InlineData("+46.70.123.45.67")]
    public void A_real_phone_number_is_valid(string value)
        => Assert.True(IsValid(value));

    [Theory]
    [InlineData("hello")]            // letters, no digits
    [InlineData("070 CALL ME")]      // letters mixed with digits
    [InlineData("070-123-456x12")]   // extension letter rejected (stricter than PhoneAttribute)
    [InlineData("12345")]            // too few digits (< 6)
    [InlineData("1234567890123456")] // too many digits (> 15)
    [InlineData("++4670123456")]     // malformed leading symbols
    public void A_non_empty_value_that_is_not_a_real_phone_number_is_invalid(string value)
        => Assert.False(IsValid(value));
}
