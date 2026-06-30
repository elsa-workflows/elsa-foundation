using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class DefaultSecretResolver(
    ISecretRepository repository,
    ISecretNameValidator nameValidator,
    ISecretManager secretManager,
    SecretLifecyclePolicy lifecyclePolicy,
    ISecretAuditSink auditSink,
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretResolver
{
    public async ValueTask<ResolvedSecret> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference.Name))
            return await FailureAsync(reference.Name, SecretResolutionFailureCode.NotFound, "Secret name is required.", cancellationToken);

        var secret = await repository.FindAsync(nameValidator.Normalize(reference.Name), cancellationToken);
        var decision = lifecyclePolicy.EvaluateRuntimeResolution(secret, reference);

        if (!decision.Allowed)
            return await FailureAsync(secret?.Name ?? reference.Name, ToResolutionFailureCode(decision.FailureCode), decision.Reason, cancellationToken);

        var resolvedSecret = secret!;
        var versionDecision = lifecyclePolicy.EvaluateRuntimeVersion(resolvedSecret);

        if (!versionDecision.Allowed)
            return await FailureAsync(resolvedSecret.Name, ToResolutionFailureCode(versionDecision.FailureCode), versionDecision.Reason, cancellationToken);

        try
        {
            var payload = await secretManager.ResolvePayloadAsync(resolvedSecret, cancellationToken);
            if (payload.Value is null)
                return await FailureAsync(resolvedSecret.Name, SecretResolutionFailureCode.CorruptState, "Secret store returned an empty value.", cancellationToken);

            await auditSink.RecordAsync(new("resolve", resolvedSecret.Name, "succeeded", timeProvider.GetUtcNow()), cancellationToken);
            return ResolvedSecret.Success(payload.Value, mapper.Map(resolvedSecret));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return await FailureAsync(resolvedSecret.Name, SecretResolutionFailureCode.StoreUnavailable, e.Message, cancellationToken);
        }
    }

    private async ValueTask<ResolvedSecret> FailureAsync(string? name, SecretResolutionFailureCode code, string error, CancellationToken cancellationToken)
    {
        await auditSink.RecordAsync(new("resolve", name ?? "", "failed", timeProvider.GetUtcNow(), Reason: code.ToString()), cancellationToken);
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
