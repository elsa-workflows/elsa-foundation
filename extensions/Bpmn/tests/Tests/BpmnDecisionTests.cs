using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Testing;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDecisionTests
{
    [Fact]
    public void BpmnDecision_declares_its_outcome_as_echoed_to_output() =>
        // The trimmed outcome becomes both the result and the completion outcome, so it cannot hold a secret.
        SecretBindingTestSupport.AssertRefusesSecretBinding(
            typeof(BpmnDecision),
            nameof(BpmnDecision.Outcome),
            SecretBindingRefusalReason.EchoedToOutput);
}
