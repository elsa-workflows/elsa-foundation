using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests.Fixtures;

/// <summary>
/// A withheld envelope as a secret read leaves it in runtime state (spec 188), for the readers that must refuse it or
/// render it without a value.
/// </summary>
internal static class WithheldValues
{
    public static readonly ValueProtectionPolicy SecretPolicy =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);

    public static readonly RuntimeSecretReference Reference = new("payments.api-key");

    public static ValueEnvelope Secret(ValueTypeDescriptor type, ValueConversionPlan? conversionPlan = null) =>
        ValueEnvelope.Withheld(type, WithheldValue.SecretReference(Reference, conversionPlan), SecretPolicy);
}
