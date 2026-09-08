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

    /// <summary>
    /// Per-operation SMTP timeout in seconds (post-launch security review, finding #12). MailKit
    /// defaults to 120 s, which is far too generous here: the guest acknowledgement and the admin
    /// notification are both awaited inline in the public <c>/boka</c> submission, so a hung SMTP
    /// endpoint would hold the guest's request — and a request thread — for minutes.
    ///
    /// <para>15 s is comfortably above a normal One.com send (well under a second) while turning an
    /// unresponsive server into a prompt, logged failure. Worst case across the retry budget is now
    /// roughly 45 s per message rather than six minutes.</para>
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;
}
