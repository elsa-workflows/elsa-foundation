using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.AgentCompositionProof;

internal sealed record CollaborationStartInput(
    string RequestText,
    string ConversationId,
    string TenantId,
    string PersistenceScope,
    string Authority);

internal sealed record AgentObservation(
    string InvocationId,
    int RoundId,
    string MessageId,
    string OutputFingerprint,
    IReadOnlyList<ResolvedReferenceSnapshot> ResolvedResources,
    AgentConfigurationReference? Configuration,
    UsageAccounting? Usage,
    string[]? ContextMessageIds);

internal sealed record ManagerDecisionRecord(
    string DecisionId,
    string TriggeringInvocationId,
    string Choice,
    int FollowUpRoundsUsed,
    string? ToolRequestId,
    IReadOnlyList<ResolvedReferenceSnapshot> TriggerResourceSnapshots,
    AgentConfigurationReference Configuration,
    UsageAccounting Usage);

internal sealed record ToolActionRequest(
    string RequestId,
    string ToolName,
    string ArgumentsJson,
    string ArgumentsFingerprint,
    string RequiredCapability,
    string IdempotencyKey,
    AgentConfigurationReference ToolConfiguration);

internal sealed record ToolApprovalRecord(
    string RequestId,
    string ArgumentsFingerprint,
    string RequiredCapability,
    string ApprovedBy,
    string ApprovalIdentity);

internal sealed record ToolInvocation(ToolActionRequest Request, ToolApprovalRecord Approval);
internal sealed record ToolActionResult(string RequestId, string ToolName, string ArgumentsFingerprint, string EffectFingerprint, string Output);

/// <summary>
/// Candidate collaboration semantics composed over WorkProof work/result rows. This policy is an experiment, not
/// an Elsa production API. It keeps route IDs, conversation IDs, resource references, configuration, tool
/// capabilities, approval, and usage as separate persisted data.
/// </summary>
internal sealed class AgentCollaborationPolicy(int usageBudget = 8) : ICompositionPolicy
{
    private const int FollowUpLimit = 1;
    private const int AgentWorkUsageCost = 1;
    private const int ManagerDecisionUsageCost = 1;
    private const string ToolActionInvocationId = "tool-action-1";
    private readonly AgentConfigurationReference _managerConfiguration =
        new("Manager", "decision-rule-v1", "deterministic-policy-v1", "tool-proposal-only");

    public string Id => "candidate-agent-collaboration-composite-v1";
    public int ManagerDecisionEvaluations { get; private set; }

    public CompositionPlan Start(string inputJson)
    {
        CollaborationStartInput input = ProofJson.Deserialize<CollaborationStartInput>(inputJson);
        if (string.IsNullOrWhiteSpace(input.RequestText) || string.IsNullOrWhiteSpace(input.ConversationId))
            throw new InvalidOperationException("Collaboration requires a request and a conversation channel.");
        if (usageBudget < 0) throw new InvalidOperationException("Usage budget cannot be negative.");

        AgentEnvelope before = SequenceEnvelope("sequence-before", input, "collect-request", []);
        var configurations = new[]
        {
            Config("Researcher"), Config("Specialist"), Config("Reviewer"), _managerConfiguration, Config("Sequence"),
            new AgentConfigurationReference("Tool:create-review-ticket", "tool-contract-v1", "deterministic-test-tool-v1", "ticket:create")
        };
        var state = new CollaborationState(
            "SequenceBefore", input, usageBudget, 0, 0, [before], [], [], [], null, null, null, configurations, false);
        return Plan(state, [Work(before)], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        CollaborationState state = ProofJson.Deserialize<CollaborationState>(stateJson);
        if (state.ProcessedInvocationIds.Contains(receipt.InvocationId, StringComparer.Ordinal))
            return Plan(state, [], state.Completed);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException($"Collaboration cannot advance from '{receipt.Status}'.");

        if (state.Stage == "ToolRunning")
            return ApplyToolResult(state, receipt);

        AgentExecutionResult result = ProofJson.Deserialize<AgentExecutionResult>(receipt.ResultJson);
        AgentEnvelope? expected = state.ActiveMessages.SingleOrDefault(x => x.InvocationId == receipt.InvocationId);
        if (expected is null || !MatchesRoute(expected, result))
            throw new InvalidOperationException("Collaboration result does not match an active typed route.");

        string[] processed = state.ProcessedInvocationIds.Append(receipt.InvocationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        AgentObservation[] observations = state.AcceptedObservations.Append(Observe(result)).ToArray();
        AgentEnvelope[] active = state.ActiveMessages.Where(x => x.InvocationId != receipt.InvocationId).ToArray();
        int usageUsed = state.UsageUsed;

        switch (state.Stage)
        {
            case "SequenceBefore":
                if (active.Length != 0) throw new InvalidOperationException("The enclosing Sequence before-child must be the only active work.");
                AgentEnvelope[] firstRound = ParticipantRound(state.Input, 0, "r1");
                if (!CanAdmit(state.UsageUsed, state.UsageBudget, AgentWorkUsageCost * firstRound.Length))
                    return Plan(state with { Stage = "BudgetExhausted", UsageUsed = usageUsed, ActiveMessages = [],
                        AcceptedObservations = observations, ProcessedInvocationIds = processed, Completed = true }, [], true);
                return Plan(state with { Stage = "AgentRound1", ActiveMessages = firstRound,
                    AcceptedObservations = observations, ProcessedInvocationIds = processed }, firstRound.Select(Work).ToArray(), false);

            case "AgentRound1":
            case "AgentRound2":
                usageUsed = AddAgentUsage(state.UsageUsed, result);
                if (active.Length > 0)
                    return Plan(state with { ActiveMessages = active, UsageUsed = usageUsed,
                        AcceptedObservations = observations, ProcessedInvocationIds = processed }, [], false);
                if (!CanAdmit(usageUsed, state.UsageBudget, AgentWorkUsageCost))
                    return Plan(state with { Stage = "BudgetExhausted", ActiveMessages = [], UsageUsed = usageUsed,
                        AcceptedObservations = observations, ProcessedInvocationIds = processed, Completed = true }, [], true);
                AgentEnvelope reviewer = ReviewerEnvelope(state with { AcceptedObservations = observations }, result.RoundId);
                string reviewerStage = result.RoundId == 0 ? "ReviewerRound1" : "ReviewerRound2";
                return Plan(state with { Stage = reviewerStage, ActiveMessages = [reviewer], UsageUsed = usageUsed,
                    AcceptedObservations = observations, ProcessedInvocationIds = processed }, [Work(reviewer)], false);

            case "ReviewerRound1":
            case "ReviewerRound2":
                usageUsed = AddAgentUsage(state.UsageUsed, result);
                if (active.Length != 0) throw new InvalidOperationException("Reviewer is a single receiver at the round barrier.");
                if (!CanAdmit(usageUsed, state.UsageBudget, ManagerDecisionUsageCost))
                    return Plan(state with { Stage = "BudgetExhausted", UsageUsed = usageUsed, ActiveMessages = [],
                        AcceptedObservations = observations, ProcessedInvocationIds = processed, Completed = true }, [], true);

                string choice = EvaluateManagerDecision(result.RoundId, state.FollowUpRoundsUsed);
                usageUsed = checked(usageUsed + ManagerDecisionUsageCost);
                UsageAccounting managerUsage = new(0, 0, ManagerDecisionUsageCost, "deterministic-manager-decision-unit");
                int followUpCount = state.FollowUpRoundsUsed;
                ToolActionRequest? pendingTool = null;
                AgentEnvelope[] nextMessages = [];
                string nextStage;
                string? toolRequestId = null;
                bool budgetExhausted = false;
                if (choice == "follow-up")
                {
                    AgentEnvelope[] followUpMessages = ParticipantRound(state.Input, 1, "r2");
                    if (CanAdmit(usageUsed, state.UsageBudget, AgentWorkUsageCost * followUpMessages.Length))
                    {
                        nextStage = "AgentRound2";
                        nextMessages = followUpMessages;
                        followUpCount++;
                    }
                    else
                    {
                        nextStage = "BudgetExhausted";
                        budgetExhausted = true;
                    }
                }
                else
                {
                    nextStage = "AwaitingToolApproval";
                    pendingTool = CreateToolRequest(state.Input);
                    toolRequestId = pendingTool.RequestId;
                }
                var decision = new ManagerDecisionRecord($"manager-decision-{state.Decisions.Length + 1}", receipt.InvocationId,
                    choice, followUpCount, toolRequestId, result.ResolvedResources, _managerConfiguration, managerUsage);
                return Plan(state with { Stage = nextStage, ActiveMessages = nextMessages, UsageUsed = usageUsed,
                    FollowUpRoundsUsed = followUpCount, AcceptedObservations = observations,
                    ProcessedInvocationIds = processed, Decisions = state.Decisions.Append(decision).ToArray(),
                    PendingToolRequest = pendingTool, Completed = budgetExhausted },
                    nextMessages.Select(Work).ToArray(), budgetExhausted);

            case "SequenceAfter":
                if (active.Length != 0) throw new InvalidOperationException("The enclosing Sequence after-child must be the only active work.");
                return Plan(state with { Stage = "Completed", ActiveMessages = [], AcceptedObservations = observations,
                    ProcessedInvocationIds = processed, Completed = true }, [], true);

            default:
                throw new InvalidOperationException($"No agent result is valid while collaboration is in stage '{state.Stage}'.");
        }
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Agent collaboration has no continuation '{continuationId}'.");

    public static CompositionPlan CreateApprovalPlan(string stateJson, string requestId, string toolName,
        string argumentsJson, string requiredCapability, string approvedBy)
    {
        CollaborationState state = ProofJson.Deserialize<CollaborationState>(stateJson);
        ToolActionRequest expected = state.PendingToolRequest ?? throw new InvalidOperationException("No tool request is awaiting approval.");
        string fingerprint = ResourceReferenceResolver.Sha256(argumentsJson);
        if (state.Stage != "AwaitingToolApproval"
            || requestId != expected.RequestId
            || toolName != expected.ToolName
            || fingerprint != expected.ArgumentsFingerprint
            || argumentsJson != expected.ArgumentsJson
            || requiredCapability != expected.RequiredCapability)
            throw new UnauthorizedAccessException("Approval does not match the exact persisted tool request, arguments, and capability.");

        string approvalIdentity = $"approval:{requestId}:{fingerprint}:{requiredCapability}";
        var approval = new ToolApprovalRecord(requestId, fingerprint, requiredCapability, approvedBy, approvalIdentity);
        var invocation = new ToolInvocation(expected, approval);
        return Plan(state with { Stage = "ToolRunning", ActiveMessages = [], Approval = approval },
            [new WorkDefinition(ToolActionInvocationId, AgentToolWorker.HandlerId, AgentToolWorker.HandlerVersion, ProofJson.Serialize(invocation))], false);
    }

    private string EvaluateManagerDecision(int reviewerRound, int followUpRoundsUsed)
    {
        ManagerDecisionEvaluations++;
        if (reviewerRound == 0 && followUpRoundsUsed == 0) return "follow-up";
        if (reviewerRound == 1 && followUpRoundsUsed == FollowUpLimit) return "propose-tool";
        throw new InvalidOperationException("Manager feedback exceeded the single follow-up round limit.");
    }

    private static CompositionPlan ApplyToolResult(CollaborationState state, WorkReceipt receipt)
    {
        if (state.Stage != "ToolRunning" || state.PendingToolRequest is null || state.Approval is null)
            throw new InvalidOperationException("Tool result has no durable approved request.");
        if (receipt.InvocationId != ToolActionInvocationId)
            throw new InvalidOperationException("Tool result receipt does not match the approved tool work invocation.");
        ToolActionResult result = ProofJson.Deserialize<ToolActionResult>(receipt.ResultJson);
        if (result.RequestId != state.PendingToolRequest.RequestId
            || result.ToolName != state.PendingToolRequest.ToolName
            || result.ArgumentsFingerprint != state.PendingToolRequest.ArgumentsFingerprint
            || result.ArgumentsFingerprint != state.Approval.ArgumentsFingerprint)
            throw new InvalidOperationException("Tool result does not match the exact approved request.");
        string[] processed = state.ProcessedInvocationIds.Append(receipt.InvocationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        AgentEnvelope after = SequenceEnvelope("sequence-after", state.Input, $"tool-output:{result.Output}", []);
        return Plan(state with { Stage = "SequenceAfter", ActiveMessages = [after], ToolResult = result,
            ProcessedInvocationIds = processed }, [Work(after)], false);
    }

    private static bool CanAdmit(int current, int budget, int cost)
        => current <= budget && cost >= 0 && cost <= budget - current;

    private static int AddAgentUsage(int current, AgentExecutionResult result)
    {
        if (result.Usage is null || result.Usage.TotalUnits != AgentWorkUsageCost)
            throw new InvalidOperationException($"Agent result usage must match the deterministic test cost of {AgentWorkUsageCost} unit.");
        return checked(current + AgentWorkUsageCost);
    }

    private static AgentObservation Observe(AgentExecutionResult result)
        => new(result.InvocationId, result.RoundId, result.MessageId, result.OutputFingerprint,
            result.ResolvedResources, result.Configuration, result.Usage, result.ContextMessageIds);

    private static AgentEnvelope[] ParticipantRound(CollaborationStartInput input, int round, string suffix)
        =>
        [
            ParticipantEnvelope("Researcher", round, $"researcher-{suffix}", $"researcher-message-{suffix}", input,
                $"research:{input.RequestText}:round:{round}"),
            ParticipantEnvelope("Specialist", round, $"specialist-{suffix}", $"specialist-message-{suffix}", input,
                $"specialist:{input.RequestText}:round:{round}")
        ];

    private static AgentEnvelope ParticipantEnvelope(string agent, int round, string invocation, string messageId,
        CollaborationStartInput input, string payload)
        => new(agent, round, invocation, messageId, payload, ParticipantReferences(agent, input), ConversationId: input.ConversationId,
            Configuration: Config(agent));

    private static AgentEnvelope ReviewerEnvelope(CollaborationState state, int round)
    {
        string suffix = round == 0 ? "r1" : "r2";
        AgentObservation[] inputs = state.AcceptedObservations.Where(x => x.RoundId == round
            && (x.InvocationId.StartsWith("researcher-", StringComparison.Ordinal)
                || x.InvocationId.StartsWith("specialist-", StringComparison.Ordinal))).ToArray();
        if (inputs.Length != 2 || inputs.Select(x => x.InvocationId.StartsWith("researcher-", StringComparison.Ordinal)).Distinct().Count() != 2)
            throw new InvalidOperationException($"Reviewer cannot start until both Researcher and Specialist observations are accepted for round {round}; saw {string.Join(',', state.AcceptedObservations.Select(x => $"{x.InvocationId}@{x.RoundId}"))}.");
        string[] contextIds = inputs.Select(x => x.MessageId).Order(StringComparer.Ordinal).ToArray();
        string context = string.Join(";", inputs.OrderBy(x => x.InvocationId, StringComparer.Ordinal).Select(x => $"{x.InvocationId}={x.OutputFingerprint}"));
        return new AgentEnvelope("Reviewer", round, $"reviewer-{suffix}", $"reviewer-message-{suffix}", context,
            ReviewerReferences(state.Input), ConversationId: state.Input.ConversationId, ContextMessageIds: contextIds,
            Configuration: Config("Reviewer"));
    }

    private static IReadOnlyList<ResourceReference> ParticipantReferences(string agent, CollaborationStartInput input)
        =>
        [
            new MemoryReference(input.TenantId, input.PersistenceScope, input.Authority, $"{agent.ToLowerInvariant()}-private", "r2",
                MemoryVisibility.Private, agent, null, MemoryPermission.Read, MemoryConsistency.Snapshot),
            new MemoryReference(input.TenantId, input.PersistenceScope, input.Authority, "shared-conversation", "r5",
                MemoryVisibility.ConversationShared, null, input.ConversationId, MemoryPermission.Read, MemoryConsistency.Snapshot),
            new KnowledgeReference(input.TenantId, input.PersistenceScope, input.Authority, "dataset-runbook", "r3")
        ];

    private static IReadOnlyList<ResourceReference> ReviewerReferences(CollaborationStartInput input)
        =>
        [
            new MemoryReference(input.TenantId, input.PersistenceScope, input.Authority, "reviewer-private", "r2",
                MemoryVisibility.Private, "Reviewer", null, MemoryPermission.Read, MemoryConsistency.Snapshot),
            new MemoryReference(input.TenantId, input.PersistenceScope, input.Authority, "shared-conversation", "r5",
                MemoryVisibility.ConversationShared, null, input.ConversationId, MemoryPermission.Read, MemoryConsistency.Snapshot),
            new KnowledgeReference(input.TenantId, input.PersistenceScope, input.Authority, "dataset-runbook", "r3")
        ];

    private static ToolActionRequest CreateToolRequest(CollaborationStartInput input)
    {
        const string args = "{\"category\":\"agent-review\",\"title\":\"Review collaboration result\"}";
        return new ToolActionRequest("review-ticket-1", "create-review-ticket", args, ResourceReferenceResolver.Sha256(args),
            "ticket:create", $"agent-collaboration/{input.ConversationId}/review-ticket-1",
            new AgentConfigurationReference("Tool:create-review-ticket", "tool-contract-v1", "deterministic-test-tool-v1", "ticket:create"));
    }

    private static AgentEnvelope SequenceEnvelope(string invocation, CollaborationStartInput input, string payload,
        IReadOnlyList<ResourceReference> references)
        => new("Sequence", 0, invocation, $"message:{invocation}", payload, references, ConversationId: input.ConversationId,
            Configuration: Config("Sequence"));

    private static AgentConfigurationReference Config(string agent)
        => new(agent, $"{agent.ToLowerInvariant()}-prompt-v1", "deterministic-test-model-v1", $"{agent.ToLowerInvariant()}-tools-v1");

    private static WorkDefinition Work(AgentEnvelope envelope)
        => new(envelope.InvocationId, AgentWorker.HandlerId, AgentWorker.HandlerVersion, ProofJson.Serialize(envelope));

    private static bool MatchesRoute(AgentEnvelope expected, AgentExecutionResult result)
        => result.InvocationId == expected.InvocationId
            && result.ReceiverId == expected.ReceiverId
            && result.RoundId == expected.RoundId
            && result.MessageId == expected.MessageId
            && result.Configuration == expected.Configuration;

    private static CompositionPlan Plan(CollaborationState state, IReadOnlyList<WorkDefinition> work, bool complete)
        => new(ProofJson.Serialize(state), work, [], complete);

    internal sealed record CollaborationState(
        string Stage,
        CollaborationStartInput Input,
        int UsageBudget,
        int UsageUsed,
        int FollowUpRoundsUsed,
        AgentEnvelope[] ActiveMessages,
        AgentObservation[] AcceptedObservations,
        string[] ProcessedInvocationIds,
        ManagerDecisionRecord[] Decisions,
        ToolActionRequest? PendingToolRequest,
        ToolApprovalRecord? Approval,
        ToolActionResult? ToolResult,
        AgentConfigurationReference[] Configurations,
        bool Completed);
}
