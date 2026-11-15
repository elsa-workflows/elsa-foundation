using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Services;
using Elsa.Secrets.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>Synthetic, time-frozen inputs shared by historical fixture capture and the real-store proof.</summary>
internal static class WeaverSchemaFixtureSeed
{
    internal const string SessionId = "fixture-session";
    internal const string Owner = "fixture-owner";
    internal const string EventId = "fixture-event";
    internal static readonly DateTimeOffset At = new(2026, 10, 1, 12, 30, 0, TimeSpan.Zero);
    internal static readonly TimeProvider Clock = new FixtureClock();
    internal static WeaverRecordCodec Codec => new(new FixtureTransport());

    internal static WeaverSessionRecord Record()
    {
        var attachment = Records.Attachment("fixture-context", JsonSerializer.SerializeToElement(new
        { nested = new { values = new[] { "a", "b" } }, number = 1.50m, text = "café \u2028" }));
        var target = new WeaverResourceTarget("fixture.module", "fixture.resource", "fixture-resource", "revision-1");
        var result = new WeaverToolResult("fixture-call", "fixture-tool", true, "Legacy result",
            new("fixture.result", "1", JsonSerializer.SerializeToElement(new { values = new[] { 1, 2 }, text = "café" })))
        {
            Receipt = new("fixture-invocation", target, target with { Revision = "revision-2" }, WeaverEffectScope.ServerPersisted,
                new("fixture.evidence", "1", JsonSerializer.SerializeToElement(new { revision = "revision-2" })))
        };
        IReadOnlyList<WeaverDisclosureSnapshot> history = [new([attachment], [result])];
        var snapshot = WeaverDisclosureHistory.Record(history[0]);
        var session = Records.Session(SessionId) with
        { CreatedAt = At, UpdatedAt = At, ExpiresAt = At.AddHours(8), Workspace = "fixture-workspace" };
        var message = Records.Message(SessionId, "fixture-message", "A frozen synthetic message: café") with
        {
            Role = WeaverRole.Assistant, CreatedAt = At, CompletedAt = At.AddSeconds(1), Status = WeaverMessageStatus.Failed,
            Error = new("fixture.legacy_error", "Synthetic legacy error", 409),
            Context = [attachment], ContextAttachmentIds = [attachment.Id], ToolResults = [result]
        };
        message = message with
        { DeliveryFingerprint = WeaverDisclosureHistory.MessageFingerprint(message.Content, message.Context, message.ToolResults, history) };
        var prepared = new WeaverPreparedToolInvocation("fixture-invocation", SessionId, "fixture-turn", "fixture-call", "fixture-tool", "1", "fixture.module", "actor-1", "tenant-1",
            target, JsonSerializer.SerializeToElement(new { values = new[] { "first", "second" } }), "", WeaverRisk.ReviewRequired,
            new("fixture.preview", "1", JsonSerializer.SerializeToElement(new { before = "café" })), WeaverExecutionLocation.Server, true, null, At.AddMinutes(10));
        prepared = prepared with { ApprovalDigest = WeaverInvocationIntegrity.PreparedDigest(prepared) };
        var proposal = Records.Proposal(SessionId, "fixture-proposal", WeaverActionProposalStatus.Executed) with
        {
            CapabilityId = "fixture-tool", CreatedAt = At, UpdatedAt = At.AddSeconds(2), ApprovedBy = "actor-1", ApprovedAt = At,
            ResourceType = target.ResourceType, ResourceId = target.ResourceId, BaseRevision = target.Revision, PreparedInvocation = prepared
        };
        var paused = new Elsa.Weaver.Core.Contracts.WeaverPausedTurn(SessionId, "fixture-turn", "fixture-proposal", "fixture-call", "fixture-tool",
            [new(WeaverRole.User, "Keep the original transcript")], 2)
        { SourceMessageId = "fixture-message", Context = [attachment], DisclosureResults = [result], DisclosureHistory = history };
        paused = paused with { DisclosureHistoryFingerprint = WeaverDisclosureHistory.PauseFingerprint(paused) };
        return new(session)
        {
            Messages = [new(message) { DisclosureDigests = [snapshot.Digest], DisclosureHistoryFingerprint = WeaverInvocationIntegrity.Digest(history) }],
            Context = [attachment], Snapshots = [snapshot],
            Proposals = [new(proposal)
            {
                Result = result, AcknowledgementDigest = "fixture-acknowledgement",
                Execution = WeaverResult<WeaverProposalExecutionResult>.Success(new("fixture-proposal", true, target.ResourceType, target.ResourceId!, "Executed") { ToolResult = result })
            }],
            PausedTurn = new(paused with { DisclosureHistory = null }) { DisclosureDigests = [snapshot.Digest] },
            Keys = [new("call", "fixture-call", "fixture-proposal")],
            PendingAudits = [Audit() with { Id = "fixture-pending", Summary = "Owed synthetic event" }]
        };
    }

    internal static WeaverAuditEvent Audit() => new(EventId, WeaverAuditEventKind.MessageAccepted, SessionId, "actor-1", "Synthetic audit: café", At,
        new Dictionary<string, string> { ["one"] = "first", ["two"] = "second" });

    internal static async Task<object> FindRowAsync(WeaverDbContext db, Type entity) => entity == typeof(WeaverSessionRow) ? await db.Set<WeaverSessionRow>().SingleAsync()
        : entity == typeof(WeaverMessageRow) ? await db.Set<WeaverMessageRow>().SingleAsync()
        : entity == typeof(WeaverContextRow) ? await db.Set<WeaverContextRow>().SingleAsync()
        : entity == typeof(WeaverProposalRow) ? await db.Set<WeaverProposalRow>().SingleAsync()
        : entity == typeof(WeaverAuditRow) ? await db.Set<WeaverAuditRow>().SingleAsync()
        : throw new InvalidOperationException("The fixture does not name an Weaver content table.");

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    /// <summary>No encryption or authentication claim: production codec framing/serialization over synthetic fixture text.</summary>
    private sealed class FixtureTransport : ISecretValueProtector
    {
        private const string Prefix = "test-only-weaver-fixture:";
        public string Protect(string value) => Prefix + value;
        public string Unprotect(string value) => value.StartsWith(Prefix, StringComparison.Ordinal)
            ? value[Prefix.Length..] : throw new InvalidDataException("Not synthetic Weaver fixture transport.");
    }
}
