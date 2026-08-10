namespace AkeroGarden.Web.Admin.Components.Shared;

/// <summary>
/// One admin toast: the text, plus whether it reports a failure. Bound by <c>Toast</c>.
///
/// <para><b>Why this is a type and not a string.</b> Every admin page used to put both outcomes
/// into the same <c>string? _message</c>, so a blocking failure — "Tilldela minst en byggnad innan
/// bokningen godkänns", "kan inte tas bort eftersom den används av en bokning" — rendered in the
/// identical green save-confirmation toast as "har sparats", bottom-right, and disappeared after
/// five seconds. The admin could easily miss that anything had failed at all (reported 2026-08-10).
/// Carrying the outcome with the text means a call site cannot state one without the other; there
/// is deliberately <b>no</b> implicit conversion from <see cref="string"/>, because a default tone
/// would silently reintroduce exactly that bug.</para>
/// </summary>
/// <param name="Text">The Swedish message shown to the admin (NFR-32). Nullable so a call site can
/// pass a result's <c>Error</c> straight through; <c>Toast</c> renders nothing for an empty one.</param>
/// <param name="IsError">True when this reports a failed action — styles the toast as an error and
/// stops it auto-dismissing.</param>
public sealed record ToastMessage(string? Text, bool IsError)
{
    /// <summary>A completed action. Auto-dismisses.</summary>
    public static ToastMessage Ok(string? text) => new(text, false);

    /// <summary>A failed or blocked action. Styled as an error and stays until something replaces it.</summary>
    public static ToastMessage Error(string? text) => new(text, true);
}
