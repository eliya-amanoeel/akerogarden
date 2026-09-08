namespace AkeroGarden.Web.Core.Services;

public interface IEmailService
{
    /// <summary>Sends an email. When <paramref name="replyToAddress"/> is set, replies route
    /// there instead of the sending mailbox — e.g. an admin notification should reply to the
    /// guest who submitted the inquiry, not back to the notification address itself.</summary>
    /// <returns>
    /// <c>true</c> if the message was handed to the SMTP server, <c>false</c> if every attempt failed
    /// and the send was abandoned.
    ///
    /// <para><strong>Why this returns a result at all.</strong> Sending is deliberately non-throwing
    /// (NFR-13) — a mail outage must never fail the booking operation that triggered it. But
    /// "swallowed" is not the same as "nothing cares": <see cref="RetentionJob"/> writes a
    /// <c>DeletionWarningEmailSentAt</c> marker to guarantee the FR-66 pre-deletion warning fires
    /// exactly once, and before this returned a status it wrote that marker whether or not the mail
    /// actually left. A silent SMTP failure therefore recorded the warning as delivered and the record
    /// was deleted 30 days later with nobody warned (post-launch security review, finding #11).</para>
    ///
    /// <para>Most callers legitimately ignore the result — for them the log entry is the whole
    /// response. Only a caller that persists "we told them" needs to check it.</para>
    /// </returns>
    Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null);
}
