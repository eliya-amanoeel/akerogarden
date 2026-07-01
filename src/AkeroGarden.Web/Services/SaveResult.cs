namespace AkeroGarden.Web.Services;

/// <summary>
/// Outcome of a create/update that may fail business validation, carrying a
/// Swedish error message for the admin UI when it does.
/// </summary>
public record SaveResult(bool Succeeded, string? Error)
{
    public static readonly SaveResult Success = new(true, null);

    public static SaveResult Fail(string error) => new(false, error);
}
