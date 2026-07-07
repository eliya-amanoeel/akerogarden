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
    public void A_real_phone_number_is_valid(string value)
        => Assert.True(IsValid(value));

    [Fact]
    public void A_non_empty_value_still_defers_to_phone_validation()
        // "hello" has no digits, so PhoneAttribute rejects it — proving the attribute only skips the
        // check for blank input, not for a filled-in nonsense value.
        => Assert.False(IsValid("hello"));
}
