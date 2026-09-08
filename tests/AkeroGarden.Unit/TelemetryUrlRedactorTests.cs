using System.Diagnostics;
using AkeroGarden.Web.Core.Security;

namespace AkeroGarden.Unit;

/// <summary>
/// URL redaction before telemetry export (post-launch security review, finding #1). Two of this
/// system's URLs carry a bearer credential — the completion magic link <c>/slutfor/{token}</c> and the
/// password-reset / invite <c>?code={payload}</c> — and both were reaching Log Analytics verbatim.
///
/// <para>These assert on both halves of the claim: that the pure function removes the credential, and
/// that the processor actually rewrites the tags on a finished <see cref="Activity"/>. Testing only the
/// string helper would prove a regex works while telemetry carried on leaking.</para>
/// </summary>
public class TelemetryUrlRedactorTests
{
    // ── The completion token ────────────────────────────────────────────────────

    [Fact]
    public void Completion_token_is_removed_from_a_full_url()
    {
        Assert.Equal(
            "https://akerogarden.se/slutfor/redacted",
            TelemetryUrlRedactor.Redact("https://akerogarden.se/slutfor/KeFeCdkvp8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [Fact]
    public void Completion_token_is_removed_from_a_bare_path()
    {
        Assert.Equal("/slutfor/redacted", TelemetryUrlRedactor.Redact("/slutfor/abc123"));
    }

    [Fact]
    public void Completion_token_is_removed_but_a_trailing_segment_is_kept()
    {
        Assert.Equal("/slutfor/redacted/tack", TelemetryUrlRedactor.Redact("/slutfor/abc123/tack"));
    }

    [Fact]
    public void Completion_token_is_removed_but_a_following_query_is_kept()
    {
        Assert.Equal(
            "/slutfor/redacted?steg=2",
            TelemetryUrlRedactor.Redact("/slutfor/abc123?steg=2"));
    }

    [Fact]
    public void Completion_route_without_a_token_is_untouched()
    {
        // The bare route 404s rather than carrying a credential; nothing to redact.
        Assert.Equal("/slutfor/", TelemetryUrlRedactor.Redact("/slutfor/"));
    }

    [Fact]
    public void Completion_match_is_case_insensitive()
    {
        Assert.Equal("/SlutFor/redacted", TelemetryUrlRedactor.Redact("/SlutFor/abc123"));
    }

    // ── The password-reset / invite code ────────────────────────────────────────

    [Fact]
    public void Reset_code_is_removed_when_it_opens_the_query_string()
    {
        Assert.Equal(
            "https://admin.akerogarden.se/aterstall-losenord?code=redacted",
            TelemetryUrlRedactor.Redact(
                "https://admin.akerogarden.se/aterstall-losenord?code=CfDJ8Nq6protectedpayload"));
    }

    [Fact]
    public void Reset_code_is_removed_when_it_follows_another_parameter()
    {
        Assert.Equal(
            "/aterstall-losenord?x=1&code=redacted",
            TelemetryUrlRedactor.Redact("/aterstall-losenord?x=1&code=CfDJ8Nq6payload"));
    }

    [Fact]
    public void Reset_code_is_removed_from_a_bare_query_string()
    {
        // url.query is exported on its own under the stable HTTP semantic conventions.
        Assert.Equal("code=redacted", TelemetryUrlRedactor.Redact("code=CfDJ8Nq6payload"));
    }

    [Fact]
    public void Parameters_after_the_code_are_preserved()
    {
        Assert.Equal(
            "/aterstall-losenord?code=redacted&retur=/konton",
            TelemetryUrlRedactor.Redact("/aterstall-losenord?code=CfDJ8payload&retur=/konton"));
    }

    [Fact]
    public void A_parameter_merely_ending_in_code_is_not_touched()
    {
        // "postcode=" must not match — the lookbehind requires the parameter to start at ? or &.
        const string url = "/nagot?postcode=12345";
        Assert.Equal(url, TelemetryUrlRedactor.Redact(url));
    }

    // ── Everything else is left alone ───────────────────────────────────────────

    [Theory]
    [InlineData("https://akerogarden.se/")]
    [InlineData("/boka?incheckning=2026-10-01")]
    [InlineData("/bokningar/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/login?ReturnUrl=%2Fbokningar")]
    [InlineData("")]
    public void Ordinary_urls_are_unchanged(string url)
    {
        Assert.Equal(url, TelemetryUrlRedactor.Redact(url));
    }

    [Fact]
    public void Both_credentials_in_one_url_are_both_removed()
    {
        Assert.Equal(
            "/slutfor/redacted?code=redacted",
            TelemetryUrlRedactor.Redact("/slutfor/sometoken?code=somepayload"));
    }

    // ── The processor — the half that actually protects telemetry ───────────────

    [Fact]
    public void Processor_rewrites_every_url_bearing_tag()
    {
        using var activity = new Activity("request");
        activity.SetTag("url.full", "https://akerogarden.se/slutfor/sometoken");
        activity.SetTag("url.path", "/slutfor/sometoken");
        activity.SetTag("url.query", "code=somepayload");
        activity.SetTag("http.url", "https://admin.akerogarden.se/x?code=somepayload");
        activity.SetTag("http.target", "/slutfor/sometoken");

        new UrlRedactingProcessor().OnEnd(activity);

        Assert.Equal("https://akerogarden.se/slutfor/redacted", activity.GetTagItem("url.full"));
        Assert.Equal("/slutfor/redacted", activity.GetTagItem("url.path"));
        Assert.Equal("code=redacted", activity.GetTagItem("url.query"));
        Assert.Equal("https://admin.akerogarden.se/x?code=redacted", activity.GetTagItem("http.url"));
        Assert.Equal("/slutfor/redacted", activity.GetTagItem("http.target"));
    }

    [Fact]
    public void Processor_leaves_unrelated_tags_and_clean_urls_alone()
    {
        using var activity = new Activity("request");
        activity.SetTag("url.path", "/boka");
        activity.SetTag("http.route", "/slutfor/{token}");
        activity.SetTag("http.response.status_code", 200);

        new UrlRedactingProcessor().OnEnd(activity);

        Assert.Equal("/boka", activity.GetTagItem("url.path"));
        // The route TEMPLATE names the parameter rather than its value, so it is not a credential
        // and must survive — it is what makes the redacted requests still groupable in the portal.
        Assert.Equal("/slutfor/{token}", activity.GetTagItem("http.route"));
        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public void Processor_tolerates_an_activity_with_no_url_tags()
    {
        using var activity = new Activity("background-job");

        new UrlRedactingProcessor().OnEnd(activity);

        Assert.Null(activity.GetTagItem("url.path"));
    }
}
