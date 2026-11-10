using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using static Elsa.Weaver.Core.Services.WeaverProviderPrimitives;

namespace Elsa.Weaver.Tests;

/// <summary>
/// A provider with no external dependencies that drives a scripted multi-step turn based on prompt keywords.
/// It exists to validate the turn orchestrator end-to-end (tool calls, tool-result feedback, proposal pauses)
/// without a real model. The tools it names ("echo", "apply-change") are resolved from the registry by the
/// orchestrator, so callers register matching <see cref="IWeaverTool"/>s to exercise behavior.
/// </summary>
internal sealed class DeterministicWeaverProvider : IWeaverProvider
{
    public const string Id = "deterministic-test";

    public const string ReadOnlyToolName = "echo";
    public const string MutatingToolName = "apply-change";

    public string ProviderId => Id;

    public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "deterministic",
            ["status"] = "available"
        }));

    public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var content = context.LatestUserMessage?.Content ?? string.Empty;

        // A two-tool plan exercised across multiple steps.
        if (Contains(content, "multi-step"))
        {
            if (context.StepIndex == 0)
            {
                yield return ToolCall("call-1", ReadOnlyToolName, """{"text":"first"}""", WeaverRisk.ReadOnly, false);
                yield break;
            }

            if (context.StepIndex == 1)
            {
                yield return ToolCall("call-2", ReadOnlyToolName, """{"text":"second"}""", WeaverRisk.ReadOnly, false);
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
            yield return ToolCall("call-1", ReadOnlyToolName, """{"text":"hello"}""", WeaverRisk.ReadOnly, false);
            yield break;
        }

        if (Contains(content, "mutate") || Contains(content, "apply change"))
        {
            yield return ToolCall("call-1", MutatingToolName, """{"change":"add-activity"}""", WeaverRisk.ReviewRequired, true);
            yield break;
        }

        yield return Delta("Deterministic agent provider response.");
    }

    public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverToolApprovalResult(request.Approved, request.Approved ? "Tool approval accepted by deterministic provider." : "Tool approval denied by deterministic provider."));

    public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverProviderDiagnostics(
            ProviderId,
            IsAvailable: true,
            Status: "Deterministic provider is available for backend contract validation.",
            WeaverProviderKind.ProviderSdkBinding,
            [WeaverProviderOperation.Chat, WeaverProviderOperation.Streaming, WeaverProviderOperation.ToolApproval],
            WeaverProviderRiskProfile.ReadOnly,
            new Dictionary<string, string> { ["adapter"] = "deterministic" }));

    private static WeaverStreamEvent Delta(string content)
        => new(NewId(), WeaverStreamEventKind.MessageDelta, content, null, null, DateTimeOffset.UtcNow, WeaverResultKind.Message);

    private static WeaverStreamEvent ToolCall(string toolCallId, string toolName, string arguments, WeaverRisk risk, bool requiresApproval)
        => new(NewId(), WeaverStreamEventKind.ToolCallRequested, null, toolCallId, null, DateTimeOffset.UtcNow, null, new WeaverToolCallRequest(toolCallId, toolName, arguments, risk, requiresApproval));

    private static bool Contains(string content, string value) => content.Contains(value, StringComparison.OrdinalIgnoreCase);

}
