using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Testing;

/// <summary>
/// Shared arrangements and assertions for the secret binding refusals (spec 188, slice 2): a compiled secret read as
/// a hand-built or imported artifact would carry it, and the declarations and backstops that must refuse it.
/// </summary>
public static class SecretBindingTestSupport
{
    /// <summary>A compiled secret read on <paramref name="inputKey"/>, as it reaches a reader that skips publication.</summary>
    public static RuntimeInputBinding SecretRead(string inputKey, string typeAlias = "String") =>
        new(
            inputKey,
            new ValueTypeDescriptor(typeAlias),
            new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
            RuntimeInputBindingSource.SecretRead,
            secret: new RuntimeSecretReference("payments.api-key"));

    /// <summary>Asserts the activity type declares that <paramref name="inputKey"/> refuses a secret reference for <paramref name="reason"/>.</summary>
    public static void AssertRefusesSecretBinding(Type activityType, string inputKey, SecretBindingRefusalReason reason) =>
        Assert.Contains(
            activityType.GetCustomAttributes<RefusesSecretBindingAttribute>(inherit: true),
            attribute => attribute.InputKey == inputKey && attribute.Reason == reason);

    /// <summary>Asserts a publish-time literal reader refuses a secret read with the fixed <c>VF-ACT-012</c> message.</summary>
    public static void AssertReaderRefusesSecretRead(Action read, string nodeId, string inputKey) =>
        AssertFixedAtPublishRefusal(Assert.ThrowsAny<ArgumentException>(read), nodeId, inputKey);

    /// <inheritdoc cref="AssertReaderRefusesSecretRead"/>
    public static async Task AssertReaderRefusesSecretReadAsync(Func<Task> read, string nodeId, string inputKey) =>
        AssertFixedAtPublishRefusal(await Assert.ThrowsAnyAsync<ArgumentException>(read), nodeId, inputKey);

    private static void AssertFixedAtPublishRefusal(ArgumentException exception, string nodeId, string inputKey) =>
        Assert.Contains(
            SecretBindingDiagnostics.SecretBindingRefused(nodeId, inputKey, SecretBindingRefusalReason.FixedAtPublish).Message,
            exception.Message,
            StringComparison.Ordinal);
}
