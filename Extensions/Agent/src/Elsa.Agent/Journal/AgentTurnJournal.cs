using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Agent.Journal;

internal sealed class AgentTurnJournal(IAgentTurnStore store, ISecretValueProtector protector, IPersistenceAccessContextAccessor access, AgentAdmissionPolicy? admissionPolicy = null)
{
    private const string RequestPurpose = "elsa.agent.turn/request/1";
    private const string ResultPurpose = "elsa.agent.turn/result/1";
    private const string DeliveryRequestPurpose = "elsa.agent.turn/request/2";
    private const string DeliveryResultPurpose = "elsa.agent.turn/result/2";

    public async ValueTask<string> StageAsync(AgentTurnRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(request.Identity);
        var limits = AgentEffectiveLimits.For(request);
        var hash = RequestHash(request, DeliveryRequestPurpose);
        var key = AgentTurnDocument.CalculateOperationKey(request.Identity);
        if (request.Continuation is not null && !request.Descriptor.AllowContinuity)
            throw Failure("agent.session.disallowed", "This published agent does not allow conversation continuity.");
        var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var candidate = new AgentTurnDocument(handle, key, hash, request.Identity,
            protector.Protect(JsonSerializer.Serialize(new RequestEnvelope(DeliveryRequestPurpose, request.Identity, hash, request), AgentJson.Options)),
            now.AddMilliseconds(limits.TimeoutMilliseconds)) { MaximumModelCalls = limits.MaximumModelCalls,
                Conversation = request.Descriptor.AllowContinuity ? request.Continuation ?? new(handle, 0) : null,
                DeliveryProtocol = 1, Deliveries = [new(request.Identity)] };
        try { return (await store.StageAsync(candidate, admissionPolicy?.AllowNewOperations ?? true, cancellationToken)).Handle; }
        catch (AgentJournalException exception) when (exception.Code == "agent.journal.admission-disabled")
        { throw Failure("agent.turn.admission-disabled", "New agent operations are disabled for this host.", exception); }
        catch (AgentJournalException exception) when (exception.Code == "agent.journal.cancelled")
        { throw Failure("agent.turn.cancelled", "This native parent has a retained cancellation and cannot admit another model turn.", exception); }
        catch (AgentJournalException exception) when (exception.Code == "agent.journal.conflict")
        {
            throw Failure("agent.turn.conflict", "The native retry does not match the retained operation fingerprint or supported delivery protocol.", exception);
        }
    }

    public async ValueTask<(AgentTurnDocument Document, AgentTurnRequest Request)> ReadAsync(string handle, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        var document = await store.ReadAsync(handle, identity, cancellationToken) ?? throw Failure("agent.journal.missing", "The staged agent turn is unavailable in this execution scope.");
        if (document.ContentPurgedAt is not null)
            throw Failure("agent.journal.content-unavailable", "The agent operation is retained, but its protected content has expired.");
        RequestEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RequestEnvelope>(protector.Unprotect(document.ProtectedRequest!), AgentJson.Options); }
        catch (Exception exception) { throw Failure("agent.journal.protection", "The staged agent request cannot be authenticated.", exception); }
        if (envelope is null || envelope.Purpose != (document.DeliveryProtocol == 1 ? DeliveryRequestPurpose : RequestPurpose) || envelope.Identity != document.Identity || envelope.Request.Identity != document.Identity ||
            !AgentTurnDeliveryRules.SameLogical(document.Identity, identity) || !AgentTurnDeliveryRules.Admits(document, identity) || envelope.Hash != document.RequestHash ||
            RequestHash(envelope.Request, envelope.Purpose) != document.RequestHash ||
            document.MaximumModelCalls != AgentEffectiveLimits.For(envelope.Request).MaximumModelCalls ||
            document.Conversation != (envelope.Request.Descriptor.AllowContinuity ? envelope.Request.Continuation ?? new(document.Handle, 0) : null))
            throw Failure("agent.journal.protection", "The staged agent request does not match this native invocation.");
        return (document, envelope.Request with { Identity = identity });
    }

    public async ValueTask<AgentTurnResult> ResultAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken,
        Action<AgentTurnRequest>? verifyRequest = null)
    {
        var (document, request) = await ReadAsync(handle, identity, cancellationToken);
        verifyRequest?.Invoke(request);
        if (document.Status != AgentTurnStatus.Accepted || !BoundToChild(document, identity, childInvocationId) || document.ProtectedResult is null)
            throw Failure("agent.turn.unaccepted", "This native child has no accepted agent turn.");
        ResultEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<ResultEnvelope>(protector.Unprotect(document.ProtectedResult), AgentJson.Options); }
        catch (Exception exception) { throw Failure("agent.journal.protection", "The agent result cannot be authenticated.", exception); }
        if (envelope is null || envelope.Purpose != (document.DeliveryProtocol == 1 ? DeliveryResultPurpose : ResultPurpose) || envelope.Identity != document.Identity || envelope.RequestHash != document.RequestHash ||
            envelope.ChildInvocationId != document.ChildInvocationId || envelope.Fence != document.Fence || envelope.AdmissionGeneration != document.AdmissionGeneration ||
            (document.DeliveryProtocol == 1 ? envelope.DispatchIdentity != AgentTurnDeliveryRules.DispatchIdentity(document) : envelope.DispatchIdentity is not null))
            throw Failure("agent.journal.protection", "The accepted agent result does not match this native invocation.");
        return envelope.Result;
    }

    internal async ValueTask<AgentTurnResult> ReplayAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        _ = await store.ReplayAcceptedAsync(handle, identity, childInvocationId, cancellationToken)
            ?? throw Failure("agent.turn.unaccepted", "This registered native delivery cannot bind the retained accepted receipt.");
        return await ResultAsync(handle, identity, childInvocationId, cancellationToken);
    }

    public async ValueTask<AgentTurnDocument> ClaimAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        var claimed = await store.ClaimAsync(handle, identity, childInvocationId, Guid.NewGuid().ToString("N"), now, cancellationToken);
        if (claimed is not null) return claimed;
        var current = await store.ReadAsync(handle, identity, cancellationToken);
        if (current?.Status == AgentTurnStatus.Cancelled)
            throw Failure("agent.turn.cancelled", "The retained operation was cancelled; the same operation cannot silently create another provider request.");
        if (current?.Status == AgentTurnStatus.Failed)
            throw Failure("agent.turn.rejected", "The earlier agent operation was rejected; the same operation cannot silently create another provider request.");
        if (current?.Status == AgentTurnStatus.OutcomeUnknown)
            throw Failure("agent.turn.outcome-unknown", "The earlier agent dispatch has no accepted outcome; a new provider request was refused.");
        if (current is { Status: AgentTurnStatus.Prepared, Conversation: not null } && current.Deadline > now)
            throw Failure("agent.conversation.conflict", "The requested conversation revision is unavailable for this native owner and agent version.");
        throw Failure("agent.turn.claim", "The agent turn is already owned, terminal, expired or unavailable; dispatch was refused.");
    }

    internal async ValueTask<AgentTurnDocument> ReadStateAsync(AgentTurnDocument expected, CancellationToken cancellationToken)
    {
        EnsureScope(expected.Identity);
        var current = await store.ReadAsync(expected.Handle, expected.Identity, cancellationToken);
        if (current is null || current.ProtectedRequest != expected.ProtectedRequest || current.RequestHash != expected.RequestHash ||
            current.Deadline != expected.Deadline || current.Conversation != expected.Conversation || current.MaximumModelCalls != expected.MaximumModelCalls)
            throw Failure("agent.journal.protection", "The admitted agent request changed while waiting for its existing claim.");
        return current;
    }

    public ValueTask<bool> DispatchAsync(AgentTurnDocument document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.DispatchAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), now, cancellationToken);
    }

    public ValueTask<bool> AcceptAsync(AgentTurnDocument document, AgentTurnResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        if (document.Conversation is { } conversation && (result.Session is not { } session || session.ConversationId != conversation.ConversationId || session.Revision != conversation.ExpectedRevision + 1))
            throw Failure("agent.session.admission", "The candidate session does not match the admitted conversation revision.");
        var envelope = new ResultEnvelope(document.DeliveryProtocol == 1 ? DeliveryResultPurpose : ResultPurpose, document.Identity, document.RequestHash,
            document.ChildInvocationId!, document.Fence!, result, document.AdmissionGeneration,
            document.DeliveryProtocol == 1 ? AgentTurnDeliveryRules.DispatchIdentity(document) : null);
        return store.AcceptAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), protector.Protect(JsonSerializer.Serialize(envelope, AgentJson.Options)), now, cancellationToken);
    }

    public async ValueTask<AgentTurnRequest> PrepareSessionAsync(AgentTurnDocument document, AgentTurnRequest request, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        if (!AgentTurnDeliveryRules.OwnsClaim(document, request.Identity))
            throw Failure("agent.session.admission", "Conversation history requires the exact admitted native request.");
        if (document.Conversation is not { } admission) return request;
        var conversation = await store.ReadConversationAsync(admission.ConversationId, document.Identity, cancellationToken);
        if (conversation is null || conversation.Revision != admission.ExpectedRevision || conversation.ActiveTurnHandle != document.Handle ||
            conversation.ActiveFence != document.Fence || conversation.ActiveAdmissionGeneration != document.AdmissionGeneration || conversation.OutcomeUnknown)
            throw Failure("agent.session.admission", "This worker does not hold the admitted conversation revision.");
        AgentSessionReference? previous = null;
        if (admission.ExpectedRevision > 0)
        {
            var accepted = conversation.AcceptedTurn ?? throw Failure("agent.session.unavailable", "The accepted conversation history is unavailable.");
            previous = (await ResultAsync(accepted.Handle, accepted.Identity, accepted.ChildInvocationId, cancellationToken)).Session;
            if (previous is null || previous.ConversationId != admission.ConversationId || previous.Revision != admission.ExpectedRevision)
                throw Failure("agent.session.unavailable", "The accepted conversation history does not match this revision.");
        }
        return request with { PreparedSession = previous, SessionTarget = new(admission.ConversationId, admission.ExpectedRevision + 1) };
    }

    public ValueTask FailAsync(AgentTurnDocument document, string code, bool unknown, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.FailAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), code, unknown, cancellationToken);
    }

    private void EnsureScope(AgentInvocationIdentity identity)
    {
        var scope = access.Current.RequireScope();
        if (scope.Value != identity.PersistencePartition) throw Failure("agent.scope", "The agent turn does not belong to the selected execution scope.");
        access.Current.EnsureTenantScope(identity.TenantId);
    }
    private static bool BoundToChild(AgentTurnDocument document, AgentInvocationIdentity identity, string child) =>
        document.DeliveryProtocol == 0 ? document.Identity == identity && document.ChildInvocationId == child :
        document.Deliveries!.Any(x => x.Identity == identity && x.ChildInvocationId == child);

    private static string RequestHash(AgentTurnRequest request, string purpose) => Hash(JsonSerializer.Serialize(
        purpose == DeliveryRequestPurpose ? request with { Identity = request.Identity with { ActivityExecutionId = "" } } : request, AgentJson.Options));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static AgentTurnException Failure(string code, string message, Exception? innerException = null) => AgentModelRunner.Failure(code, message, innerException);
    private sealed record RequestEnvelope(string Purpose, AgentInvocationIdentity Identity, string Hash, AgentTurnRequest Request);
    private sealed record ResultEnvelope(string Purpose, AgentInvocationIdentity Identity, string RequestHash, string ChildInvocationId, string Fence, AgentTurnResult Result, int AdmissionGeneration = 0, AgentInvocationIdentity? DispatchIdentity = null);
}
