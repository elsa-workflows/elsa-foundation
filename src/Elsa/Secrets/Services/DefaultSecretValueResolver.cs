using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

/// <summary>
/// Default <see cref="ISecretValueResolver"/>: applies the lifecycle policy to the referenced secret
/// and reads the payload from the per-provider store. Owns the whole resolution path (it does not
/// route through <see cref="ISecretManager"/>, which is the lifecycle facade only).
/// </summary>
public sealed class DefaultSecretValueResolver(
    ISecretRepository repository,
    ISecretNameValidator nameValidator,
    ISecretStoreRegistry storeRegistry,
    SecretLifecyclePolicy lifecyclePolicy,
    ISecretAuditSink auditSink,
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretValueResolver
{
    public async ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (string.IsNullOrWhiteSpace(reference.Name))
            return await FailureAsync(tenantId, reference.Name, SecretResolutionFailureCode.NotFound, "Secret name is required.", cancellationToken);

        var secret = await repository.FindAsync(tenantId, nameValidator.Normalize(reference.Name), cancellationToken);
        var decision = lifecyclePolicy.EvaluateRuntimeResolution(secret, reference);

        if (!decision.Allowed)
            return await FailureAsync(tenantId, secret?.Name ?? reference.Name, ToResolutionFailureCode(decision.FailureCode), decision.Reason, cancellationToken);

        var resolvedSecret = secret!;
        var versionDecision = lifecyclePolicy.EvaluateRuntimeVersion(resolvedSecret);

        if (!versionDecision.Allowed)
            return await FailureAsync(tenantId, resolvedSecret.Name, ToResolutionFailureCode(versionDecision.FailureCode), versionDecision.Reason, cancellationToken);

        try
        {
            var store = storeRegistry.Get(resolvedSecret.StoreName);
            var payload = await store.ReadAsync(new SecretReadContext(resolvedSecret, versionDecision.Version!), cancellationToken);

            if (payload is null)
                return await FailureAsync(tenantId, resolvedSecret.Name, SecretResolutionFailureCode.StoreUnavailable, "Secret payload could not be resolved.", cancellationToken);

            if (payload.Value is null)
                return await FailureAsync(tenantId, resolvedSecret.Name, SecretResolutionFailureCode.CorruptState, "Secret store returned an empty value.", cancellationToken);

            await auditSink.RecordAsync(new("resolve", resolvedSecret.Name, "succeeded", timeProvider.GetUtcNow(), tenantId), cancellationToken);
            return ResolvedSecret.Success(payload.Value, mapper.Map(resolvedSecret));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return await FailureAsync(tenantId, resolvedSecret.Name, SecretResolutionFailureCode.StoreUnavailable, e.Message, cancellationToken);
        }
    }

    private async ValueTask<ResolvedSecret> FailureAsync(string tenantId, string? name, SecretResolutionFailureCode code, string error, CancellationToken cancellationToken)
    {
        await auditSink.RecordAsync(new("resolve", name ?? "", "failed", timeProvider.GetUtcNow(), tenantId, Reason: code.ToString()), cancellationToken);
        return ResolvedSecret.Failure(code, error);
    }

    private static SecretResolutionFailureCode ToResolutionFailureCode(SecretLifecycleFailureCode failureCode) => failureCode switch
    {
        SecretLifecycleFailureCode.Deleted or SecretLifecycleFailureCode.NotFound => SecretResolutionFailureCode.NotFound,
        SecretLifecycleFailureCode.Inactive or SecretLifecycleFailureCode.NoActiveVersion => SecretResolutionFailureCode.Inactive,
        SecretLifecycleFailureCode.Expired => SecretResolutionFailureCode.Expired,
        SecretLifecycleFailureCode.Revoked => SecretResolutionFailureCode.Revoked,
        SecretLifecycleFailureCode.TypeMismatch => SecretResolutionFailureCode.TypeMismatch,
        SecretLifecycleFailureCode.ScopeMismatch => SecretResolutionFailureCode.ScopeMismatch,
        _ => SecretResolutionFailureCode.Inactive
    };
}
