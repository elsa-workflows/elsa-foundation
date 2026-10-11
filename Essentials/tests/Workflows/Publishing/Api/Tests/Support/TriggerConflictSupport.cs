using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// Claims an Exclusive trigger stimulus in another slot, so a candidate that claims the same stimulus hits a real
/// preflight/publish trigger conflict rather than a canned throw. Shared by every HTTP test that needs one
/// (issues #1659, #1699).
/// </summary>
internal static class TriggerConflictSupport
{
    private const string StimulusType = "Http";
    private const string StimulusHash = "get:/orders";

    public static async Task ClaimExclusiveTriggerInAnotherSlotAsync(
        IWorkflowActivationAuthority authority,
        IWorkflowTriggerBindingStore triggerStore,
        StubTriggerExtractor extractor,
        string definitionId,
        string slotName,
        string activationId,
        DateTimeOffset now)
    {
        var current = await authority.FindAsync(definitionId, slotName);
        var transition = await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            definitionId, slotName, activationId, WorkflowActivationSource.Publishing, current?.Revision ?? 0, now));
        Assert.True(transition.Succeeded, transition.Diagnostic);
        await triggerStore.SaveAsync(ExclusiveBinding(definitionId, $"artifact-{slotName}", $"{slotName}-node", now) with
        {
            ActivationId = activationId,
            SlotId = WorkflowActivationSlotIdentity.Create(definitionId, slotName)
        });
        extractor.ClaimsExclusiveTrigger = true;
    }

    private static WorkflowTriggerBinding ExclusiveBinding(string definitionId, string artifactId, string executableNodeId, DateTimeOffset now) =>
        new(
            WorkflowTriggerBinding.BuildId(artifactId, executableNodeId, StimulusHash),
            artifactId,
            definitionId,
            "1.0",
            "capture-hash",
            executableNodeId,
            StimulusType,
            StimulusHash,
            CorrelationScope: null,
            new Dictionary<string, string>(),
            now,
            Cardinality: TriggerCardinality.Exclusive);

    /// <summary>Claims one Exclusive stimulus on the candidate's root node once a test asks for a trigger clash.</summary>
    internal sealed class StubTriggerExtractor : IWorkflowTriggerBindingExtractor
    {
        public bool ClaimsExclusiveTrigger { get; set; }

        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) =>
            ClaimsExclusiveTrigger
                ? [ExclusiveBinding(executable.Identity.DefinitionId, executable.Identity.ArtifactId, executable.RootActivity.ExecutableNodeId, DateTimeOffset.UtcNow)]
                : [];
    }
}
