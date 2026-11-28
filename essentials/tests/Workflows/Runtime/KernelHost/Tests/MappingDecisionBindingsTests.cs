using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Native;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class MappingDecisionBindingsTests
{
    [Theory]
    [InlineData(ExecutableDecisionPinMetadata.HistoricalKey, """{"inputProtectionVersion":1}""", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, "{}", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, """{"inputProtectionVersion":1,"inputProtectionVersion":1}""", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, """{"inputProtectionVersion":1}""", true)]
    public void A_decision_only_materializes_workflow_inputs_verified_by_its_behavioral_pin(string key, string pin, bool verified)
    {
        var decision = Node("decision", EvaluateDecision.ActivityType,
        [
            Literal(nameof(EvaluateDecision.Decision), "asset@1", "String"),
            Request(nameof(EvaluateDecision.Inputs), "PolicyInput"),
            RequestMember(nameof(EvaluateDecision.OperationBudget), "PolicyOptions", "budget")
        ], metadata: new Dictionary<string, string> { [key] = pin });
        var model = NativeExecutableMapper.Map(Executable(decision), KernelHostCapabilities.All with { Decisions = true });

        Assert.IsType<LiteralBinding>(model.Root.Inputs[nameof(EvaluateDecision.Decision)]);
        Assert.Equal(verified, model.Root.Inputs.ContainsKey(nameof(EvaluateDecision.Inputs)));
        Assert.Equal(verified, model.Root.Inputs.ContainsKey(nameof(EvaluateDecision.OperationBudget)));
    }
}
