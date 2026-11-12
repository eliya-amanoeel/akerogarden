namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Configuration for booking-related emails (§9): the links they contain and the footer
/// details every email must carry (Åkerögården name, contact email, privacy-policy link).
/// Bound from the "BookingEmail" configuration section.
/// </summary>
public class BookingEmailOptions
{
    /// <summary>Public site base URL, e.g. <c>https://akerogarden.se</c> — used to build the
    /// completion magic link and the privacy-policy link in the footer.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Admin panel base URL, e.g. <c>https://admin.akerogarden.se</c> — used to build
    /// the direct booking link in the admin notification email (FR-06).</summary>
    public string AdminBaseUrl { get; set; } = string.Empty;

    /// <summary>Address that receives admin notification emails (new inquiry, etc.).</summary>
    public string AdminNotificationEmail { get; set; } = string.Empty;

    /// <summary>Display name for the admin notification recipient.</summary>
    public string AdminNotificationName { get; set; } = "Åkerögården Admin";

    /// <summary>Contact email shown in every email footer (§9).</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Åkerögården's phone number in the Swedish domestic form, as <c>/kontakt</c> shows it. Held once
    /// here and read by every place that shows the number — <c>/kontakt</c> and the booking terms'
    /// contact section — so a new number is one edit. A code default, like the payment details below:
    /// it changes only through a reviewed PR and a deploy (client review 2026-11-11, item 3).
    /// </summary>
    public string ContactPhone { get; set; } = "073-952 78 73";

    /// <summary>The number in E.164 form for a <c>tel:</c> link (<c>+46739527873</c>), so the link
    /// also works when dialled from abroad.</summary>
    public string ContactPhoneTel => "+46" + NationalDigits;

    /// <summary>The number in international form grouped in threes (<c>+46 739 527 873</c>), the way
    /// the booking terms write it.</summary>
    public string ContactPhoneInternational => "+46 " + string.Join(' ', NationalDigits.Chunk(3).Select(c => new string(c)));

    /// <summary>The digits after the trunk prefix: "073-952 78 73" → "739527873".</summary>
    private string NationalDigits => new string(ContactPhone.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');

    /// <summary>Property address stated in the 14-day reminder (FR-64). Configurable so the
    /// go-live address can be set without a code change.</summary>
    public string PropertyAddress { get; set; } = "Åkerögården, Norrtälje";

    // ── Where guests pay the deposit (FR-115) ───────────────────────────────────
    // The client's answers of 2026-10-08. Code defaults on purpose, like PropertyAddress: they change
    // only through a reviewed PR and a deploy, and never from the admin panel — anyone with an admin
    // login could otherwise change where guests send money. Not secrets (they are printed in every
    // deposit email), so no Key Vault.

    /// <summary>Payee shown to the guest — the organisation's name as the privacy policy gives it.</summary>
    public string PaymentRecipient { get; set; } = "Kaldeiska katolska Mar Narsay Missionen i Södertälje";

    public string PaymentBankgiro { get; set; } = "5713-3399";

    public string PaymentSwish { get; set; } = "123 583 24 23";

    /// <summary>SEB clearing number; the client's "53641087228" split by the maintainer (2026-10-08).</summary>
    public string PaymentClearingNumber { get; set; } = "5364";

    public string PaymentAccountNumber { get; set; } = "108 72 28";

    public PaymentDetails Payment =>
        new(PaymentRecipient, PaymentBankgiro, PaymentSwish, PaymentClearingNumber, PaymentAccountNumber);
}

/// <summary>The payment details printed in the deposit emails (FR-115/116).</summary>
public record PaymentDetails(
    string Recipient, string Bankgiro, string Swish, string ClearingNumber, string AccountNumber);
