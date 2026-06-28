using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace AkeroGarden.Unit;

public class PasswordResetCodeTests
{
    private static IDataProtector CreateProtector() =>
        new EphemeralDataProtectionProvider().CreateProtector("PasswordReset");

    [Fact]
    public void PasswordResetCode_RoundTrip_PreservesEmailAndToken()
    {
        var protector = CreateProtector();
        const string email = "admin@akerogarden.se";
        const string token = "CfDJ8KNxTFDOxmMa3hIBkgJzVWabcdef123";

        var code = protector.Protect($"{email}|{token}");
        var payload = protector.Unprotect(code);
        var sep = payload.IndexOf('|');

        Assert.True(sep > 0);
        Assert.Equal(email, payload[..sep]);
        Assert.Equal(token, payload[(sep + 1)..]);
    }

    [Fact]
    public void PasswordResetCode_DoesNotContainEmailInPlaintext()
    {
        var protector = CreateProtector();
        const string email = "admin@akerogarden.se";

        var code = protector.Protect($"{email}|some-identity-token");

        // NFR-05: email must not be readable from the URL parameter
        Assert.DoesNotContain(email, code);
    }

    [Fact]
    public void PasswordResetCode_TamperedCode_ThrowsCryptographicException()
    {
        var protector = CreateProtector();

        // A tampered or randomly constructed code must not be accepted
        Assert.Throws<CryptographicException>(() =>
            protector.Unprotect("tampered-or-forged-code"));
    }

    [Fact]
    public void PasswordResetCode_WrongPurpose_CannotUnprotect()
    {
        var provider = new EphemeralDataProtectionProvider();
        var resetProtector = provider.CreateProtector("PasswordReset");
        var otherProtector = provider.CreateProtector("SomethingElse");

        var code = resetProtector.Protect("admin@akerogarden.se|token");

        // A protector with a different purpose string cannot read the code
        Assert.Throws<CryptographicException>(() => otherProtector.Unprotect(code));
    }
}
