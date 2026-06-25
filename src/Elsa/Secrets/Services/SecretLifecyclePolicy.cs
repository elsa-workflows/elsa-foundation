using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class SecretLifecyclePolicy
{
    public SecretLifecycleDecision EvaluatePublicVisibility(Secret? secret)
    {
        if (secret is null)
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.NotFound, "Secret not found.");

        if (secret.Status == SecretStatus.Deleted)
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.Deleted, "Secret not found.");

        return SecretLifecycleDecision.Allow();
    }

    public SecretLifecycleDecision EvaluatePublicOperation(Secret? secret) => EvaluatePublicVisibility(secret);
}

public readonly record struct SecretLifecycleDecision(bool Allowed, SecretLifecycleFailureCode FailureCode, string Reason)
{
    public static SecretLifecycleDecision Allow() => new(true, SecretLifecycleFailureCode.None, "");
    public static SecretLifecycleDecision Deny(SecretLifecycleFailureCode failureCode, string reason) => new(false, failureCode, reason);
}

public enum SecretLifecycleFailureCode
{
    None,
    NotFound,
    Deleted
}
