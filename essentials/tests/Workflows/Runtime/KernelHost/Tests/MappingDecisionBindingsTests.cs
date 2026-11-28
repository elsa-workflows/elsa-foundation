using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Native;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class MappingDecisionBindingsTests
{
    private const string PinFields = """
        "selection":"asset@1","tenantId":"tenant","assetId":"asset","versionId":"1","artifact":{"contentHash":"sha256:example"}
        """;

    [Theory]
    [InlineData(ExecutableDecisionPinMetadata.HistoricalKey, "{" + PinFields + ",\"inputProtectionVersion\":1}", "asset@1", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, "{" + PinFields + "}", "asset@1", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, """{"inputProtectionVersion":1}""", "asset@1", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, "{" + PinFields + ",\"inputProtectionVersion\":1,\"inputProtectionVersion\":1}", "asset@1", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, "{" + PinFields + ",\"inputProtectionVersion\":1}", "asset@2", false)]
    [InlineData(ExecutableDecisionPinMetadata.BehavioralKey, "{" + PinFields + ",\"inputProtectionVersion\":1}", "asset@1", true)]
    public void A_decision_only_materializes_workflow_inputs_verified_by_its_behavioral_pin(string key, string pin, string selection, bool verified)
    {
        var decision = Node("decision", EvaluateDecision.ActivityType,
        [
            Literal(nameof(EvaluateDecision.Decision), selection, "String"),
            Request(nameof(EvaluateDecision.Inputs), "PolicyInput"),
            RequestMember(nameof(EvaluateDecision.OperationBudget), "PolicyOptions", "budget")
        ], metadata: new Dictionary<string, string> { [key] = pin });
        var model = NativeExecutableMapper.Map(Executable(decision), KernelHostCapabilities.All with { Decisions = true });

        Assert.IsType<LiteralBinding>(model.Root.Inputs[nameof(EvaluateDecision.Decision)]);
        Assert.Equal(verified, model.Root.Inputs.ContainsKey(nameof(EvaluateDecision.Inputs)));
        Assert.Equal(verified, model.Root.Inputs.ContainsKey(nameof(EvaluateDecision.OperationBudget)));
    }

    [Fact]
    public void A_historical_decision_with_a_protected_selector_restores_without_materializing_it()
    {
        var selector = Literal(nameof(EvaluateDecision.Decision), "asset@1", "String");
        var protection = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External);
        var protectedSelector = new RuntimeInputBinding(selector.InputKey, selector.TargetType, protection,
            selector.Source, literal: selector.Literal);
        var decision = Node("decision", EvaluateDecision.ActivityType,
        [
            protectedSelector,
            Request(nameof(EvaluateDecision.Inputs), "PolicyInput")
        ], metadata: new Dictionary<string, string>
        {
            [ExecutableDecisionPinMetadata.BehavioralKey] = "{" + PinFields + "}"
        });

        var model = NativeExecutableMapper.Map(Executable(decision), KernelHostCapabilities.All with { Decisions = true });

        Assert.Empty(model.Root.Inputs);
    }
}
