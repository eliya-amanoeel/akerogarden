using System.Globalization;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The rules for the arrival and departure times a guest states on the inquiry form (FR-100, §2.8):
/// which values are offered, how a posted value is validated, and when one counts as a deviation
/// worth warning the admin about (FR-101). Pure and unit-testable (NFR-26).
///
/// <para><b>A request, not a commitment.</b> Nothing here prices or approves a non-standard time —
/// the surcharge conversation stays outside the system. The policy exists so the stated time is
/// usable downstream instead of being free text nobody can act on.</para>
/// </summary>
public static class StatedTimePolicy
{
    /// <summary>Earliest selectable time (§2.8, confirmed 2026-08-06).</summary>
    public static readonly TimeOnly Earliest = new(7, 0);

    /// <summary>Latest selectable time (§2.8).</summary>
    public static readonly TimeOnly Latest = new(22, 0);

    /// <summary>Selectable times are on the half hour.</summary>
    public const int StepMinutes = 30;

    /// <summary>The wire/display format. Fixed and culture-invariant so a posted value means the
    /// same thing whatever the browser's locale.</summary>
    public const string Format = @"HH\:mm";

    /// <summary>Every selectable time, in order — the dropdown's options (FR-100).</summary>
    public static IReadOnlyList<TimeOnly> Options()
    {
        var options = new List<TimeOnly>();
        for (var t = Earliest; t <= Latest; t = t.AddMinutes(StepMinutes))
        {
            options.Add(t);
            if (t == Latest) break; // AddMinutes wraps past midnight; Latest is the last option
        }
        return options;
    }

    /// <summary>The value written into an option and posted back — always <c>HH:mm</c>.</summary>
    public static string Display(TimeOnly time) => time.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>The same, for a nullable stored value, falling back to the standard when a booking
    /// predates the field or never stated one.</summary>
    public static string Display(TimeOnly? time, TimeOnly standard) => Display(time ?? standard);

    /// <summary>True if <paramref name="time"/> is one of the offered values — in range and on the
    /// half hour.</summary>
    public static bool IsSelectable(TimeOnly time) =>
        time >= Earliest && time <= Latest && time.Minute % StepMinutes == 0 && time.Second == 0;

    /// <summary>
    /// Parses a posted value, server-authoritatively (mirrors <see cref="InquiryNormalizer"/>).
    /// Anything that is not one of the offered values — unparseable, out of range, off the half
    /// hour — falls back to <paramref name="standard"/>, because the dropdown cannot produce such a
    /// value: a request carrying one was crafted, and "no time validly stated" is the truthful
    /// reading of it. Deliberately not snapped to the nearest option, which would invent a claim
    /// the guest never made.
    /// </summary>
    public static TimeOnly Normalize(string? posted, TimeOnly standard) =>
        TryParse(posted, out var parsed) ? parsed : standard;

    /// <summary>
    /// Parses a posted value into one of the offered times. False for anything else — used where
    /// there is a stored value worth keeping, so an unusable post leaves it alone instead of
    /// overwriting it with a default (the admin edit path, FR-48).
    /// </summary>
    public static bool TryParse(string? posted, out TimeOnly time)
    {
        if (TimeOnly.TryParseExact(posted, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && IsSelectable(parsed))
        {
            time = parsed;
            return true;
        }

        time = default;
        return false;
    }

    /// <summary>
    /// The Swedish deviation warning for the admin (FR-101), or null when the time matches the
    /// standard. Non-blocking — it exists so the surcharge conversation is not missed, since an
    /// approved booking would otherwise imply the stated time was approved along with it.
    /// </summary>
    public static string? Deviation(string label, TimeOnly? stated, TimeOnly standard) =>
        stated is { } time && time != standard
            ? $"{label} {Display(time)} avviker från standard {Display(standard)}."
            : null;
}
