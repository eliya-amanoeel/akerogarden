using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Configuration for the public inquiry form's bot check (NFR-04). Bound from the
/// "InquiryFormGuard" configuration section.
/// </summary>
public class InquiryFormGuardOptions
{
    /// <summary>A form sent sooner than this after the page was rendered is refused: no person fills
    /// in a name, an address and a message in under three seconds, and a form-filling bot does.</summary>
    public int MinimumFillSeconds { get; set; } = 3;

    /// <summary>A stamp older than this is refused, so one harvested stamp cannot be replayed forever.
    /// A guest who left the tab open overnight sends again once — the form keeps what they typed.</summary>
    public int MaximumAgeHours { get; set; } = 24;
}

/// <summary>The two hidden fields each public inquiry form posts alongside its model. Bound on the
/// page, not added to <c>StayRequestInput</c>/<c>GeneralInquiryInput</c>: those are also the admin
/// panel's create-booking models, where no bot check belongs.</summary>
public class InquiryFormGuardInput
{
    /// <summary>The signed render time issued by <see cref="InquiryFormGuard.Issue"/>.</summary>
    public string? Stamp { get; set; }

    /// <summary>The trap field: hidden from people, keyboards, screen readers and autofill, so only
    /// something that fills in every field it finds ever sends a value.</summary>
    public string? Extra { get; set; }
}

/// <summary>Why a submission was refused, or that it was not. Logged — never shown to the visitor,
/// who sees one message for every refusal.</summary>
public enum InquiryFormGuardVerdict
{
    Accepted,
    TrapFilled,
    StampMissing,
    StampInvalid,
    TooFast,
    Expired,
}

/// <summary>
/// The public inquiry form's bot check (NFR-04). A submission is accepted only if it shows signs of a
/// person filling it in on our page: the trap field is empty, and the signed render stamp is genuine,
/// at least <see cref="InquiryFormGuardOptions.MinimumFillSeconds"/> old and at most
/// <see cref="InquiryFormGuardOptions.MaximumAgeHours"/> old.
///
/// <para><b>Why it exists:</b> from 2026-11-15 a bot posted the general-inquiry form several times a
/// day with random strings and a stranger's address — email bombing, which uses many sites' forms to
/// flood one victim's inbox, and our acknowledgement (FR-05) was the payload. The per-IP rate limit
/// cannot see it: each victim's flood comes from many addresses, a few submissions each.</para>
///
/// <para>The stamp is protected with the host's Data Protection key ring, which is persisted to Blob
/// Storage, so a stamp rendered before a restart or a deploy still verifies after it. It carries no
/// personal data — only the moment the form was rendered.</para>
/// </summary>
public sealed class InquiryFormGuard(
    IDataProtectionProvider dataProtection, IOptions<InquiryFormGuardOptions> options, TimeProvider clock)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AkeroGarden.InquiryFormGuard.v1");
    private readonly InquiryFormGuardOptions _opts = options.Value;

    private TimeSpan MinimumFill => TimeSpan.FromSeconds(_opts.MinimumFillSeconds);
    private TimeSpan MaximumAge => TimeSpan.FromHours(_opts.MaximumAgeHours);

    /// <summary>A new stamp holding the current time.</summary>
    public string Issue() =>
        _protector.Protect(clock.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The stamp to render on a form. A form redrawn after a post keeps the stamp it was sent with while
    /// that stamp is still good, so a guest correcting a validation error is not made to wait out the
    /// minimum fill time a second time; anything else gets a fresh one.
    /// </summary>
    public string StampFor(string? postedStamp) =>
        IssuedAt(postedStamp) is { } issuedAt && clock.GetUtcNow() - issuedAt <= MaximumAge
            ? postedStamp!
            : Issue();

    public InquiryFormGuardVerdict Check(InquiryFormGuardInput? posted)
    {
        if (!string.IsNullOrEmpty(posted?.Extra))
            return InquiryFormGuardVerdict.TrapFilled;
        var stamp = posted?.Stamp;
        if (string.IsNullOrEmpty(stamp))
            return InquiryFormGuardVerdict.StampMissing;
        if (IssuedAt(stamp) is not { } issuedAt)
            return InquiryFormGuardVerdict.StampInvalid;

        var age = clock.GetUtcNow() - issuedAt;
        if (age > MaximumAge)
            return InquiryFormGuardVerdict.Expired;
        // A negative age (a stamp from the future) lands here too: it was not issued by this clock.
        if (age < MinimumFill)
            return InquiryFormGuardVerdict.TooFast;
        return InquiryFormGuardVerdict.Accepted;
    }

    private DateTimeOffset? IssuedAt(string? stamp)
    {
        if (string.IsNullOrEmpty(stamp))
            return null;
        try
        {
            var ticks = long.Parse(_protector.Unprotect(stamp), NumberStyles.None, CultureInfo.InvariantCulture);
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or OverflowException
                                       or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
