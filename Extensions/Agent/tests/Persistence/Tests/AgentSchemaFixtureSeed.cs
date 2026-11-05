using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Agent.Services;
using Elsa.Secrets.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.Tests;

/// <summary>Synthetic, time-frozen inputs shared by historical fixture capture and the real-store proof.</summary>
internal static class AgentSchemaFixtureSeed
{
    internal const string SessionId = "fixture-session";
    internal const string Owner = "fixture-owner";
    internal const string EventId = "fixture-event";
    internal static readonly DateTimeOffset At = new(2026, 10, 1, 12, 30, 0, TimeSpan.Zero);
    internal static readonly TimeProvider Clock = new FixtureClock();
    internal static AgentRecordCodec Codec => new(new FixtureTransport());

    internal static AgentSessionRecord Record()
    {
        var attachment = Records.Attachment("fixture-context", JsonSerializer.SerializeToElement(new
        { nested = new { values = new[] { "a", "b" } }, number = 1.50m, text = "café \u2028" }));
        var target = new AgentResourceTarget("fixture.module", "fixture.resource", "fixture-resource", "revision-1");
        var result = new AgentToolResult("fixture-call", "fixture-tool", true, "Legacy result",
            new("fixture.result", "1", JsonSerializer.SerializeToElement(new { values = new[] { 1, 2 }, text = "café" })))
        {
            Receipt = new("fixture-invocation", target, target with { Revision = "revision-2" }, AgentEffectScope.ServerPersisted,
                new("fixture.evidence", "1", JsonSerializer.SerializeToElement(new { revision = "revision-2" })))
        };
        IReadOnlyList<AgentDisclosureSnapshot> history = [new([attachment], [result])];
        var snapshot = AgentDisclosureHistory.Record(history[0]);
        var session = Records.Session(SessionId) with
        { CreatedAt = At, UpdatedAt = At, ExpiresAt = At.AddHours(8), Workspace = "fixture-workspace" };
        var message = Records.Message(SessionId, "fixture-message", "A frozen synthetic message: café") with
        {
            Role = AgentRole.Assistant, CreatedAt = At, CompletedAt = At.AddSeconds(1), Status = AgentMessageStatus.Failed,
            Error = new("fixture.legacy_error", "Synthetic legacy error", 409),
            Context = [attachment], ContextAttachmentIds = [attachment.Id], ToolResults = [result]
        };
        message = message with
        { DeliveryFingerprint = AgentDisclosureHistory.MessageFingerprint(message.Content, message.Context, message.ToolResults, history) };
        var prepared = new AgentPreparedToolInvocation("fixture-invocation", SessionId, "fixture-turn", "fixture-call", "fixture-tool", "1", "fixture.module", "actor-1", "tenant-1",
            target, JsonSerializer.SerializeToElement(new { values = new[] { "first", "second" } }), "", AgentRisk.ReviewRequired,
            new("fixture.preview", "1", JsonSerializer.SerializeToElement(new { before = "café" })), AgentExecutionLocation.Server, true, null, At.AddMinutes(10));
        prepared = prepared with { ApprovalDigest = AgentInvocationIntegrity.PreparedDigest(prepared) };
        var proposal = Records.Proposal(SessionId, "fixture-proposal", AgentActionProposalStatus.Executed) with
        {
            CapabilityId = "fixture-tool", CreatedAt = At, UpdatedAt = At.AddSeconds(2), ApprovedBy = "actor-1", ApprovedAt = At,
            ResourceType = target.ResourceType, ResourceId = target.ResourceId, BaseRevision = target.Revision, PreparedInvocation = prepared
        };
        var paused = new Elsa.Agent.Core.Contracts.AgentPausedTurn(SessionId, "fixture-turn", "fixture-proposal", "fixture-call", "fixture-tool",
            [new(AgentRole.User, "Keep the original transcript")], 2)
        { SourceMessageId = "fixture-message", Context = [attachment], DisclosureResults = [result], DisclosureHistory = history };
        paused = paused with { DisclosureHistoryFingerprint = AgentDisclosureHistory.PauseFingerprint(paused) };
        return new(session)
        {
            Messages = [new(message) { DisclosureDigests = [snapshot.Digest], DisclosureHistoryFingerprint = AgentInvocationIntegrity.Digest(history) }],
            Context = [attachment], Snapshots = [snapshot],
            Proposals = [new(proposal)
            {
                Result = result, AcknowledgementDigest = "fixture-acknowledgement",
                Execution = AgentResult<AgentProposalExecutionResult>.Success(new("fixture-proposal", true, target.ResourceType, target.ResourceId!, "Executed") { ToolResult = result })
            }],
            PausedTurn = new(paused with { DisclosureHistory = null }) { DisclosureDigests = [snapshot.Digest] },
            Keys = [new("call", "fixture-call", "fixture-proposal")],
            PendingAudits = [Audit() with { Id = "fixture-pending", Summary = "Owed synthetic event" }]
        };
    }

    internal static AgentAuditEvent Audit() => new(EventId, AgentAuditEventKind.MessageAccepted, SessionId, "actor-1", "Synthetic audit: café", At,
        new Dictionary<string, string> { ["one"] = "first", ["two"] = "second" });

    internal static async Task<object> FindRowAsync(AgentDbContext db, Type entity) => entity == typeof(AgentSessionRow) ? await db.Set<AgentSessionRow>().SingleAsync()
        : entity == typeof(AgentMessageRow) ? await db.Set<AgentMessageRow>().SingleAsync()
        : entity == typeof(AgentContextRow) ? await db.Set<AgentContextRow>().SingleAsync()
        : entity == typeof(AgentProposalRow) ? await db.Set<AgentProposalRow>().SingleAsync()
        : entity == typeof(AgentAuditRow) ? await db.Set<AgentAuditRow>().SingleAsync()
        : throw new InvalidOperationException("The fixture does not name an Agent content table.");

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    /// <summary>No encryption or authentication claim: production codec framing/serialization over synthetic fixture text.</summary>
    private sealed class FixtureTransport : ISecretValueProtector
    {
        private const string Prefix = "test-only-agent-fixture:";
        public string Protect(string value) => Prefix + value;
        public string Unprotect(string value) => value.StartsWith(Prefix, StringComparison.Ordinal)
            ? value[Prefix.Length..] : throw new InvalidDataException("Not synthetic Agent fixture transport.");
    }
}
