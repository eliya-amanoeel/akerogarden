using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The phone number is held once, in the domestic form, and the other two forms are derived from it
/// (<see cref="ContactDetails"/>). A wrong derivation would put a dead <c>tel:</c> link on
/// <c>/kontakt</c> and in every email, or a wrong number in the booking terms, so both are asserted.
/// </summary>
public class ContactDetailsTests
{
    [Fact]
    public void Tel_link_form_is_E164()
    {
        Assert.Equal("+46739527873", ContactDetails.PhoneTel);
    }

    [Fact]
    public void International_form_is_grouped_in_threes_like_the_terms()
    {
        Assert.Equal("+46 739 527 873", ContactDetails.PhoneInternational);
    }

    [Fact]
    public void Address_on_one_line_is_street_postcode_and_town()
    {
        Assert.Equal("Åkerövägen 81, 761 94 Norrtälje", ContactDetails.Address);
    }
}
