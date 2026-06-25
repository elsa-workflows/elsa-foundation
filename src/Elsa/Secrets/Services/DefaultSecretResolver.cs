using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class DefaultSecretResolver(
    ISecretRepository repository,
    ISecretNameValidator nameValidator,
    ISecretManager secretManager,
    ISecretAuditSink auditSink,
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretResolver
{
    public async ValueTask<ResolvedSecret> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference.Name))
            return await FailureAsync(reference.Name, SecretResolutionFailureCode.NotFound, "Secret name is required.", cancellationToken);

        var secret = await repository.FindAsync(nameValidator.Normalize(reference.Name), cancellationToken);

        if (secret is null)
            return await FailureAsync(reference.Name, SecretResolutionFailureCode.NotFound, "Secret not found.", cancellationToken);

        if (secret.Status == SecretStatus.Deleted)
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.NotFound, "Secret not found.", cancellationToken);

        if (reference.TypeName is not null && !string.Equals(secret.TypeName, reference.TypeName, StringComparison.OrdinalIgnoreCase))
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.TypeMismatch, "Secret type does not match the requested type.", cancellationToken);

        if (reference.Scope is not null && !string.Equals(secret.Scope, reference.Scope, StringComparison.OrdinalIgnoreCase))
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.ScopeMismatch, "Secret scope does not match the requested scope.", cancellationToken);

        if (secret.Status == SecretStatus.Revoked)
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.Revoked, "Secret has been revoked.", cancellationToken);

        if (secret.Status is SecretStatus.Retired)
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.Inactive, "Secret is not active.", cancellationToken);

        var version = secret.LatestActiveVersion;

        if (version is null)
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.Inactive, "Secret has no active version.", cancellationToken);

        if (version.IsExpired())
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.Expired, "Secret version has expired.", cancellationToken);

        try
        {
            var payload = await secretManager.ResolvePayloadAsync(secret, cancellationToken);
            if (payload.Value is null)
                return await FailureAsync(secret.Name, SecretResolutionFailureCode.CorruptState, "Secret store returned an empty value.", cancellationToken);

            await auditSink.RecordAsync(new("resolve", secret.Name, "succeeded", timeProvider.GetUtcNow()), cancellationToken);
            return ResolvedSecret.Success(payload.Value, mapper.Map(secret));
        }
        catch (Exception e)
        {
            return await FailureAsync(secret.Name, SecretResolutionFailureCode.StoreUnavailable, e.Message, cancellationToken);
        }
    }

    private async ValueTask<ResolvedSecret> FailureAsync(string? name, SecretResolutionFailureCode code, string error, CancellationToken cancellationToken)
    {
        await auditSink.RecordAsync(new("resolve", name ?? "", "failed", timeProvider.GetUtcNow(), Reason: code.ToString()), cancellationToken);
        return ResolvedSecret.Failure(code, error);
    }
}
