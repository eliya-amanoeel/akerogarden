namespace AkeroGarden.Web.Services;

/// <summary>Outcome of a completion-form submission (Phase 4 slice 3).</summary>
public enum CompletionSubmitOutcome
{
    /// <summary>Participants + meals were saved and the booking advanced to <c>DetailsComplete</c>.</summary>
    Success,

    /// <summary>The link is no longer usable (expired / already completed / superseded / not active).
    /// <see cref="CompletionSubmitResult.Access"/> says which, so the page shows the right message.</summary>
    LinkNotValid,

    /// <summary>The form had validation errors (see <see cref="CompletionSubmitResult.Errors"/>);
    /// nothing was persisted.</summary>
    ValidationFailed
}

/// <summary>Result of <see cref="BookingService.SubmitCompletionAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Access">For <see cref="CompletionSubmitOutcome.LinkNotValid"/>, why the link failed.</param>
/// <param name="Errors">For <see cref="CompletionSubmitOutcome.ValidationFailed"/>, the Swedish messages.</param>
public record CompletionSubmitResult(
    CompletionSubmitOutcome Outcome,
    CompletionAccess Access,
    IReadOnlyList<string> Errors)
{
    public static CompletionSubmitResult Ok() =>
        new(CompletionSubmitOutcome.Success, CompletionAccess.Valid, []);

    public static CompletionSubmitResult LinkFailed(CompletionAccess access) =>
        new(CompletionSubmitOutcome.LinkNotValid, access, []);

    public static CompletionSubmitResult Invalid(IReadOnlyList<string> errors) =>
        new(CompletionSubmitOutcome.ValidationFailed, CompletionAccess.Valid, errors);
}
