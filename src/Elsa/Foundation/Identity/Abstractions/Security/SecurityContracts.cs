using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Security;

public interface IAuthEventSink
{
    ValueTask EmitAsync(AuthEvent authEvent, CancellationToken cancellationToken = default);
}

public interface ISecurityDefaultGuard
{
    ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default);
}

public interface ISecurityDefaultGuardEvaluator
{
    ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default);
}

public sealed record AuthEvent(
    string Id,
    DateTimeOffset Timestamp,
    AuthEventCategory Category,
    string Actor,
    string Target,
    AuthEventOutcome Outcome,
    string? TenantId,
    IReadOnlyDictionary<string, string> Detail);

public sealed record SecurityGuardContext(
    bool IsDevelopmentOrDemo,
    string? SigningKey,
    bool RequireHttpsMetadata,
    Uri? MetadataAddress,
    CredentialRecord? Credential = null);

public sealed record SecurityGuardViolation(string Code, string Message);

public enum AuthEventCategory
{
    Authentication,
    Authorization,
    IdentityChange,
    CredentialLifecycle
}

public enum AuthEventOutcome
{
    Success,
    Denied,
    Failure
}

public sealed class SecurityDefaultGuardEvaluator(IEnumerable<ISecurityDefaultGuard> guards) : ISecurityDefaultGuardEvaluator
{
    public async ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default)
    {
        var violations = new List<SecurityGuardViolation>();

        foreach (var guard in guards)
            violations.AddRange(await guard.ValidateAsync(context, cancellationToken));

        return violations;
    }
}

public sealed class SigningKeySecurityDefaultGuard : ISecurityDefaultGuard
{
    private static readonly HashSet<string> KnownWeakKeys = new(StringComparer.Ordinal)
    {
        "00000000000000000000000000000000",
        "development",
        "secret",
        "changeme"
    };

    public ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default)
    {
        if (context.IsDevelopmentOrDemo)
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([]);

        if (string.IsNullOrWhiteSpace(context.SigningKey))
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([new("identity.signing_key.missing", "A signing key is required outside development/demo environments.")]);

        if (context.SigningKey.Length < 32 || KnownWeakKeys.Contains(context.SigningKey))
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([new("identity.signing_key.weak", "The configured signing key is too short or a known development default.")]);

        return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([]);
    }
}

public sealed class HttpsMetadataSecurityDefaultGuard : ISecurityDefaultGuard
{
    public ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default)
    {
        if (!context.RequireHttpsMetadata || context.MetadataAddress is null || context.MetadataAddress.Scheme == Uri.UriSchemeHttps)
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([]);

        return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([new("identity.metadata.https_required", "Provider metadata must use HTTPS when HTTPS metadata is required.")]);
    }
}

public sealed class SecretHashSecurityDefaultGuard(IOptions<FoundationIdentityOptions> options) : ISecurityDefaultGuard
{
    public ValueTask<IReadOnlyList<SecurityGuardViolation>> ValidateAsync(SecurityGuardContext context, CancellationToken cancellationToken = default)
    {
        if (context.Credential is null)
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([]);

        if (options.Value.AllowedSecretHashAlgorithms.Contains(context.Credential.HashAlgorithm))
            return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([]);

        return ValueTask.FromResult<IReadOnlyList<SecurityGuardViolation>>([new("identity.secret_hash.unsupported", $"Credential '{context.Credential.Id}' uses unsupported hash algorithm '{context.Credential.HashAlgorithm}'.")]);
    }
}
