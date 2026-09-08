using AkeroGarden.Web.Core.Security;

namespace AkeroGarden.Unit;

/// <summary>
/// The Content-Security-Policy string itself (Phase 11). These assert the exact production value
/// rather than "contains default-src", because every directive here is a decision someone could
/// reasonably widen later without realising what it was protecting — a test that only checks for
/// presence would stay green through exactly the change worth catching.
/// </summary>
public class SecurityPolicyTests
{
    private const string Hash = "'sha256-AAAA'";

    [Fact]
    public void Public_production_policy_is_exactly_this()
    {
        var policy = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit: false, host: "akerogarden.se", isDevelopment: false);

        Assert.Equal(
            "default-src 'self'; " +
            "script-src 'self' 'sha256-AAAA'; " +
            "style-src 'self'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "object-src 'none'",
            policy);
    }

    [Fact]
    public void Admin_production_policy_names_its_own_websocket_origin()
    {
        // The admin host is Interactive Server: without a connect-src that admits the circuit's
        // WebSocket, the panel does not work at all. 'self' covers it per CSP3, but the origin is
        // named explicitly because Safari has historically disagreed and the failure is total.
        var policy = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit: true, host: "admin.akerogarden.se", isDevelopment: false);

        Assert.Contains("connect-src 'self' wss://admin.akerogarden.se;", policy);
    }

    [Fact]
    public void Public_production_policy_admits_no_websocket_origin()
    {
        // The public host is pure Static SSR. If a wss: origin ever appears here, something has
        // quietly made the public site interactive.
        var policy = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit: false, host: "akerogarden.se", isDevelopment: false);

        Assert.DoesNotContain("wss://", policy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_production_policy_allows_inline_script(bool interactiveCircuit)
    {
        // The point of computing the import-map hash at runtime is to keep 'unsafe-inline' out of
        // script-src. Browsers ignore hashes once 'unsafe-inline' is present, so this appearing
        // would silently void the hash rather than merely widening the policy.
        var policy = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit, host: "akerogarden.se", isDevelopment: false);

        Assert.Contains("script-src 'self' 'sha256-AAAA';", policy);
        Assert.DoesNotContain("'unsafe-eval'", policy);
        Assert.DoesNotContain("script-src-elem", policy);
        // Nothing inline is permitted anywhere in the policy — not script, and (since the 19 inline
        // style attributes were converted to component modifiers) not style either. A style-src-attr
        // exemption reappearing here would mean someone re-introduced inline styling in markup.
        Assert.DoesNotContain("'unsafe-inline'", policy);
        Assert.DoesNotContain("style-src-attr", policy);
    }

    [Fact]
    public void Development_widens_connect_src_for_hot_reload_only()
    {
        // `dotnet watch` serves its browser-refresh WebSocket from a second port. This is the only
        // difference between the dev and production policies; if the two ever diverge further, the
        // E2E suite (which runs Development) stops testing the rules production actually applies.
        var development = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit: false, host: "localhost:5199", isDevelopment: true);
        var production = AkeroSecurityHeaders.BuildPolicy(
            Hash, interactiveCircuit: false, host: "localhost:5199", isDevelopment: false);

        Assert.Equal(
            production.Replace("connect-src 'self'", "connect-src 'self' ws://localhost:* wss://localhost:*"),
            development);
    }

    [Fact]
    public void A_missing_import_map_hash_leaves_script_src_at_self_alone()
    {
        // Degrades closed, not open: if the hash could not be computed, script-src must not fall
        // back to 'unsafe-inline'. The page would break loudly (the import map is blocked) rather
        // than the policy quietly admitting every inline script.
        var policy = AkeroSecurityHeaders.BuildPolicy(
            importMapHash: "", interactiveCircuit: false, host: "akerogarden.se", isDevelopment: false);

        Assert.Contains("script-src 'self';", policy);
        Assert.DoesNotContain("'unsafe-inline'", policy);
    }
}
