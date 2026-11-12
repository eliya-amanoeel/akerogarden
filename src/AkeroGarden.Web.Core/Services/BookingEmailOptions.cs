namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Configuration for booking-related emails (§9): where admin notifications go, and where guests
/// pay. Bound from the "BookingEmail" configuration section. The links the emails contain are built
/// from <see cref="SiteOptions"/>; Åkerögården's own contact details are not configuration — see
/// <see cref="ContactDetails"/>.
/// </summary>
public class BookingEmailOptions
{
    /// <summary>Address that receives admin notification emails (new inquiry, etc.).</summary>
    public string AdminNotificationEmail { get; set; } = string.Empty;

    /// <summary>Display name for the admin notification recipient.</summary>
    public string AdminNotificationName { get; set; } = "Åkerögården Admin";

    // ── Where guests pay the deposit (FR-115) ───────────────────────────────────
    // The client's answers of 2026-10-08. Code defaults on purpose: they change
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
