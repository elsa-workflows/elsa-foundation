using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Testing;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// The loops copy these inputs into their persisted iteration state (ForEach writes each collection item into its
/// iteration frame; For walks a persisted index from Start by Step to End), so each declares that it refuses a secret
/// reference and publication refuses one there (spec 188, T099; research R3a IP14).
/// </summary>
public sealed class SecretBindingRefusalTests
{
    [Theory]
    [InlineData(typeof(ForEachActivity), nameof(ForEachActivity.Collection))]
    [InlineData(typeof(ForActivity), nameof(ForActivity.Start))]
    [InlineData(typeof(ForActivity), nameof(ForActivity.End))]
    [InlineData(typeof(ForActivity), nameof(ForActivity.Step))]
    public void A_loop_declares_its_persisted_inputs_as_refusing_secret_bindings(Type activityType, string inputKey) =>
        SecretBindingTestSupport.AssertRefusesSecretBinding(activityType, inputKey, SecretBindingRefusalReason.PersistedByActivity);
}
