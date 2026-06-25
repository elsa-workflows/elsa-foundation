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

    public SecretLifecycleDecision EvaluateRuntimeResolution(Secret? secret, SecretReference reference)
    {
        var visibility = EvaluatePublicVisibility(secret);

        if (!visibility.Allowed)
            return visibility;

        var existingSecret = secret!;

        if (reference.TypeName is not null && !string.Equals(existingSecret.TypeName, reference.TypeName, StringComparison.OrdinalIgnoreCase))
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.TypeMismatch, "Secret type does not match the requested type.");

        if (reference.Scope is not null && !string.Equals(existingSecret.Scope, reference.Scope, StringComparison.OrdinalIgnoreCase))
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.ScopeMismatch, "Secret scope does not match the requested scope.");

        if (existingSecret.Status == SecretStatus.Revoked)
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.Revoked, "Secret has been revoked.");

        if (existingSecret.Status == SecretStatus.Retired)
            return SecretLifecycleDecision.Deny(SecretLifecycleFailureCode.Inactive, "Secret is not active.");

        return SecretLifecycleDecision.Allow();
    }
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
    Deleted,
    Inactive,
    Revoked,
    TypeMismatch,
    ScopeMismatch
}
