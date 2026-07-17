namespace AkeroGarden.Web.Core.Services;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;

    /// <summary>How many times to attempt a send before giving up (NFR-13). Must be ≥ 1.</summary>
    public int MaxSendAttempts { get; set; } = 3;

    /// <summary>Base delay between retry attempts, in milliseconds; scaled linearly by attempt
    /// number. Set to 0 to retry immediately (used in tests).</summary>
    public int RetryDelayMs { get; set; } = 1000;
}
