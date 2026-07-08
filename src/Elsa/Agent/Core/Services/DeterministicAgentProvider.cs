using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using static Elsa.Agent.Core.Services.AgentProviderPrimitives;

namespace Elsa.Agent.Core.Services;

/// <summary>
/// A provider with no external dependencies that drives a scripted multi-step turn based on prompt keywords.
/// It exists to validate the turn orchestrator end-to-end (tool calls, tool-result feedback, proposal pauses)
/// without a real model. The tools it names ("echo", "apply-change") are resolved from the registry by the
/// orchestrator, so callers register matching <see cref="IAgentTool"/>s to exercise behavior.
/// </summary>
public sealed class DeterministicAgentProvider : IAgentProvider
{
    public const string Id = "deterministic-test";

    public const string ReadOnlyToolName = "echo";
    public const string MutatingToolName = "apply-change";

    public string ProviderId => Id;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "deterministic",
            ["status"] = "available"
        }));

    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var content = context.LatestUserMessage?.Content ?? string.Empty;

        // A two-tool plan exercised across multiple steps.
        if (Contains(content, "multi-step"))
        {
            if (context.StepIndex == 0)
            {
                yield return ToolCall("call-1", ReadOnlyToolName, """{"text":"first"}""", AgentRisk.ReadOnly, false);
                yield break;
            }

            if (context.StepIndex == 1)
            {
                yield return ToolCall("call-2", ReadOnlyToolName, """{"text":"second"}""", AgentRisk.ReadOnly, false);
                yield break;
            }

            yield return Delta($"Completed multi-step plan with {context.PendingToolResults.Count} tool result(s).");
            yield break;
        }

        // Once tool results are available, summarize them and end the turn.
        if (context.PendingToolResults.Count > 0)
        {
            var summary = string.Join("; ", context.PendingToolResults.Select(x => x.Content));
            yield return Delta($"Tool results: {summary}");
            yield break;
        }

        if (Contains(content, "use tool") || Contains(content, "read"))
        {
            yield return ToolCall("call-1", ReadOnlyToolName, """{"text":"hello"}""", AgentRisk.ReadOnly, false);
            yield break;
        }

        if (Contains(content, "mutate") || Contains(content, "apply change"))
        {
            yield return ToolCall("call-1", MutatingToolName, """{"change":"add-activity"}""", AgentRisk.ReviewRequired, true);
            yield break;
        }

        yield return Delta("Deterministic agent provider response.");
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(request.Approved, request.Approved ? "Tool approval accepted by deterministic provider." : "Tool approval denied by deterministic provider."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            IsAvailable: true,
            Status: "Deterministic provider is available for backend contract validation.",
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming, AgentProviderOperation.ToolApproval],
            AgentProviderRiskProfile.ReadOnly,
            new Dictionary<string, string> { ["adapter"] = "deterministic" }));

    private static AgentStreamEvent Delta(string content)
        => new(NewId(), AgentStreamEventKind.MessageDelta, content, null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);

    private static AgentStreamEvent ToolCall(string toolCallId, string toolName, string arguments, AgentRisk risk, bool requiresApproval)
        => new(NewId(), AgentStreamEventKind.ToolCallRequested, null, toolCallId, null, DateTimeOffset.UtcNow, null, new AgentToolCallRequest(toolCallId, toolName, arguments, risk, requiresApproval));

    private static bool Contains(string content, string value) => content.Contains(value, StringComparison.OrdinalIgnoreCase);

}
