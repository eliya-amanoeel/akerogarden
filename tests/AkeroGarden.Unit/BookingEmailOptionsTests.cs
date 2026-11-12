using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The phone number is held once, in the domestic form, and the other two forms are derived from it
/// (client review 2026-11-11, item 3). A wrong derivation would put a dead <c>tel:</c> link on
/// <c>/kontakt</c> or a wrong number in the booking terms, so both are asserted.
/// </summary>
public class BookingEmailOptionsTests
{
    [Fact]
    public void Contact_phone_defaults_to_the_current_number()
    {
        var options = new BookingEmailOptions();
        Assert.Equal("073-952 78 73", options.ContactPhone);
    }

    [Fact]
    public void Tel_link_form_is_E164()
    {
        var options = new BookingEmailOptions();
        Assert.Equal("+46739527873", options.ContactPhoneTel);
    }

    [Fact]
    public void International_form_is_grouped_in_threes_like_the_terms()
    {
        var options = new BookingEmailOptions();
        Assert.Equal("+46 739 527 873", options.ContactPhoneInternational);
    }
}
