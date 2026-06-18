using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Security;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class SecurityDefaultGuardTests
{
    [Fact]
    public async Task GuardsRejectWeakProductionDefaults()
    {
        var evaluator = new SecurityDefaultGuardEvaluator(
        [
            new SigningKeySecurityDefaultGuard(),
            new HttpsMetadataSecurityDefaultGuard(),
            new SecretHashSecurityDefaultGuard(Options.Create(new FoundationIdentityOptions()))
        ]);
        var credential = new CredentialRecord(
            "cred-1",
            "tenant-a",
            CredentialSubjectType.Application,
            "app-1",
            CredentialKind.ClientSecret,
            "hash",
            "SHA256",
            CredentialStatus.Active,
            null);

        var violations = await evaluator.ValidateAsync(new(false, "secret", true, new Uri("http://idp.example/.well-known/openid-configuration"), credential));

        Assert.Contains(violations, x => x.Code == "identity.signing_key.weak");
        Assert.Contains(violations, x => x.Code == "identity.metadata.https_required");
        Assert.Contains(violations, x => x.Code == "identity.secret_hash.unsupported");
    }

    [Fact]
    public async Task SigningKeyGuardAllowsDevelopmentDefaultsOnlyInDevelopmentOrDemo()
    {
        var guard = new SigningKeySecurityDefaultGuard();

        var violations = await guard.ValidateAsync(new(true, "secret", true, null));

        Assert.Empty(violations);
    }

    [Fact]
    public async Task SigningKeyGuardStillRequiresADevelopmentSigningKey()
    {
        var guard = new SigningKeySecurityDefaultGuard();

        var violations = await guard.ValidateAsync(new(true, null, true, null));

        Assert.Contains(violations, x => x.Code == "identity.signing_key.missing");
    }

    [Fact]
    public async Task SecretHashGuardReportsViolationWhenAllowedAlgorithmsAreNull()
    {
        var guard = new SecretHashSecurityDefaultGuard(Options.Create(new FoundationIdentityOptions
        {
            AllowedSecretHashAlgorithms = null!
        }));
        var credential = new CredentialRecord(
            "cred-1",
            "tenant-a",
            CredentialSubjectType.Application,
            "app-1",
            CredentialKind.ClientSecret,
            "hash",
            SecretHashAlgorithms.Pbkdf2Sha256,
            CredentialStatus.Active,
            null);

        var violations = await guard.ValidateAsync(new(false, new string('x', 32), true, null, credential));

        Assert.Contains(violations, x => x.Code == "identity.secret_hash.unsupported");
    }
}
