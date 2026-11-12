namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// How to reach and find Åkerögården — the one home of every contact detail the site, the legal
/// pages, the admin handbook and every email show. A changed number or address is one edit here.
///
/// <para><b>Constants, not configuration.</b> These are facts about the business, the same in every
/// environment, so no setting can make one surface disagree with another. As settings they did:
/// the email signature read the contact address from configuration (on staging, a different
/// address) while every page typed out its own copy.</para>
/// </summary>
public static class ContactDetails
{
    /// <summary>The name guests know — the sender of every email and the signature's first line.</summary>
    public const string Name = "Åkerögården";

    /// <summary>The general contact address.</summary>
    public const string Email = "info@akerogarden.se";

    /// <summary>The address for data-protection questions and rights requests (privacy policy §1, §6).</summary>
    public const string PrivacyEmail = "dataskydd@akerogarden.se";

    /// <summary>The phone number in the Swedish domestic form, as <c>/kontakt</c> shows it.</summary>
    public const string Phone = "073-952 78 73";

    /// <summary>The number in E.164 form for a <c>tel:</c> link (<c>+46739527873</c>), so the link
    /// also works when dialled from abroad.</summary>
    public static readonly string PhoneTel = "+46" + NationalDigits(Phone);

    /// <summary>The number in international form grouped in threes (<c>+46 739 527 873</c>), the way
    /// the booking terms write it.</summary>
    public static readonly string PhoneInternational =
        "+46 " + string.Join(' ', NationalDigits(Phone).Chunk(3).Select(c => new string(c)));

    public const string Street = "Åkerövägen 81";

    public const string PostalCode = "761 94";

    public const string City = "Norrtälje";

    /// <summary>The address on one line — "Åkerövägen 81, 761 94 Norrtälje".</summary>
    public const string Address = Street + ", " + PostalCode + " " + City;

    public const string OrganisationNumber = "815601-0129";

    /// <summary>The digits after the trunk prefix: "073-952 78 73" → "739527873".</summary>
    private static string NationalDigits(string phone) =>
        new string(phone.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
}
