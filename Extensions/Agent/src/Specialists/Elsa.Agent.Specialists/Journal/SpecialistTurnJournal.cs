using Elsa.Agent.Specialists.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Agent.Specialists.Journal;

internal sealed class SpecialistTurnJournal(ISpecialistTurnStore store, ISecretValueProtector protector, IPersistenceAccessContextAccessor access)
{
    private const string RequestPurpose = "elsa.specialist.turn/request/1";
    private const string ResultPurpose = "elsa.specialist.turn/result/1";

    public async ValueTask<string> StageAsync(SpecialistTurnRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(request.Identity);
        var serialized = JsonSerializer.Serialize(request, SpecialistJson.Options);
        var hash = Hash(serialized);
        var key = SpecialistTurnDocument.CalculateOperationKey(request.Identity);
        if (request.Continuation is not null && !request.Descriptor.AllowContinuity)
            throw Failure("specialist.session.disallowed", "This published specialist does not allow conversation continuity.");
        var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var candidate = new SpecialistTurnDocument(handle, key, hash, request.Identity,
            protector.Protect(JsonSerializer.Serialize(new RequestEnvelope(RequestPurpose, request.Identity, hash, request), SpecialistJson.Options)),
            now.AddMilliseconds(request.Descriptor.Limits.TimeoutMilliseconds)) { MaximumModelCalls = request.Descriptor.Limits.MaximumModelCalls,
                Conversation = request.Descriptor.AllowContinuity ? request.Continuation ?? new(handle, 0) : null };
        return (await store.StageAsync(candidate, cancellationToken)).Handle;
    }

    public async ValueTask<(SpecialistTurnDocument Document, SpecialistTurnRequest Request)> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        var document = await store.ReadAsync(handle, identity, cancellationToken) ?? throw Failure("specialist.journal.missing", "The staged specialist turn is unavailable in this execution scope.");
        RequestEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<RequestEnvelope>(protector.Unprotect(document.ProtectedRequest), SpecialistJson.Options); }
        catch (Exception exception) { throw Failure("specialist.journal.protection", "The staged specialist request cannot be authenticated.", exception); }
        if (envelope is null || envelope.Purpose != RequestPurpose || envelope.Identity != identity || envelope.Request.Identity != identity || envelope.Hash != document.RequestHash ||
            Hash(JsonSerializer.Serialize(envelope.Request, SpecialistJson.Options)) != document.RequestHash ||
            document.MaximumModelCalls != envelope.Request.Descriptor.Limits.MaximumModelCalls ||
            document.Conversation != (envelope.Request.Descriptor.AllowContinuity ? envelope.Request.Continuation ?? new(document.Handle, 0) : null))
            throw Failure("specialist.journal.protection", "The staged specialist request does not match this native invocation.");
        return (document, envelope.Request);
    }

    public async ValueTask<SpecialistTurnResult> ResultAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken)
    {
        var (document, _) = await ReadAsync(handle, identity, cancellationToken);
        if (document.Status != SpecialistTurnStatus.Accepted || document.ChildInvocationId != childInvocationId || document.ProtectedResult is null)
            throw Failure("specialist.turn.unaccepted", "This native child has no accepted specialist turn.");
        ResultEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<ResultEnvelope>(protector.Unprotect(document.ProtectedResult), SpecialistJson.Options); }
        catch (Exception exception) { throw Failure("specialist.journal.protection", "The specialist result cannot be authenticated.", exception); }
        if (envelope is null || envelope.Purpose != ResultPurpose || envelope.Identity != identity || envelope.RequestHash != document.RequestHash ||
            envelope.ChildInvocationId != childInvocationId || envelope.Fence != document.Fence || envelope.AdmissionGeneration != document.AdmissionGeneration)
            throw Failure("specialist.journal.protection", "The accepted specialist result does not match this native invocation.");
        return envelope.Result;
    }

    public async ValueTask<SpecialistTurnDocument> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        var claimed = await store.ClaimAsync(handle, identity, childInvocationId, Guid.NewGuid().ToString("N"), now, cancellationToken);
        if (claimed is not null) return claimed;
        var current = await store.ReadAsync(handle, identity, cancellationToken);
        if (current?.Status == SpecialistTurnStatus.OutcomeUnknown)
            throw Failure("specialist.turn.outcome-unknown", "The earlier specialist dispatch has no accepted outcome; a new provider request was refused.");
        if (current is { Status: SpecialistTurnStatus.Prepared, Conversation: not null } && current.Deadline > now)
            throw Failure("specialist.conversation.conflict", "The requested conversation revision is unavailable for this native owner and specialist version.");
        throw Failure("specialist.turn.claim", "The specialist turn is already owned, terminal, expired or unavailable; dispatch was refused.");
    }

    internal async ValueTask<SpecialistTurnDocument> ReadStateAsync(SpecialistTurnDocument expected, CancellationToken cancellationToken)
    {
        EnsureScope(expected.Identity);
        var current = await store.ReadAsync(expected.Handle, expected.Identity, cancellationToken);
        if (current is null || current.ProtectedRequest != expected.ProtectedRequest || current.RequestHash != expected.RequestHash ||
            current.Deadline != expected.Deadline || current.Conversation != expected.Conversation || current.MaximumModelCalls != expected.MaximumModelCalls)
            throw Failure("specialist.journal.protection", "The admitted specialist request changed while waiting for its existing claim.");
        return current;
    }

    public ValueTask<bool> DispatchAsync(SpecialistTurnDocument document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.DispatchAsync(document.Handle, document.Identity, SpecialistTurnLeaseRules.Token(document), now, cancellationToken);
    }

    public ValueTask<bool> AcceptAsync(SpecialistTurnDocument document, SpecialistTurnResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        if (document.Conversation is { } conversation && (result.Session is not { } session || session.ConversationId != conversation.ConversationId || session.Revision != conversation.ExpectedRevision + 1))
            throw Failure("specialist.session.admission", "The candidate session does not match the admitted conversation revision.");
        var envelope = new ResultEnvelope(ResultPurpose, document.Identity, document.RequestHash, document.ChildInvocationId!, document.Fence!, result, document.AdmissionGeneration);
        return store.AcceptAsync(document.Handle, document.Identity, SpecialistTurnLeaseRules.Token(document), protector.Protect(JsonSerializer.Serialize(envelope, SpecialistJson.Options)), now, cancellationToken);
    }

    public async ValueTask<SpecialistTurnRequest> PrepareSessionAsync(SpecialistTurnDocument document, SpecialistTurnRequest request, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        if (document.Identity != request.Identity)
            throw Failure("specialist.session.admission", "Conversation history requires the exact admitted native request.");
        if (document.Conversation is not { } admission) return request;
        var conversation = await store.ReadConversationAsync(admission.ConversationId, document.Identity, cancellationToken);
        if (conversation is null || conversation.Revision != admission.ExpectedRevision || conversation.ActiveTurnHandle != document.Handle ||
            conversation.ActiveFence != document.Fence || conversation.ActiveAdmissionGeneration != document.AdmissionGeneration || conversation.OutcomeUnknown)
            throw Failure("specialist.session.admission", "This worker does not hold the admitted conversation revision.");
        SpecialistSessionReference? previous = null;
        if (admission.ExpectedRevision > 0)
        {
            var accepted = conversation.AcceptedTurn ?? throw Failure("specialist.session.unavailable", "The accepted conversation history is unavailable.");
            previous = (await ResultAsync(accepted.Handle, accepted.Identity, accepted.ChildInvocationId, cancellationToken)).Session;
            if (previous is null || previous.ConversationId != admission.ConversationId || previous.Revision != admission.ExpectedRevision)
                throw Failure("specialist.session.unavailable", "The accepted conversation history does not match this revision.");
        }
        return request with { PreparedSession = previous, SessionTarget = new(admission.ConversationId, admission.ExpectedRevision + 1) };
    }

    public ValueTask FailAsync(SpecialistTurnDocument document, string code, bool unknown, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.FailAsync(document.Handle, document.Identity, SpecialistTurnLeaseRules.Token(document), code, unknown, cancellationToken);
    }

    private void EnsureScope(SpecialistInvocationIdentity identity)
    {
        var scope = access.Current.RequireScope();
        if (scope.Value != identity.PersistencePartition) throw Failure("specialist.scope", "The specialist turn does not belong to the selected execution scope.");
        access.Current.EnsureTenantScope(identity.TenantId);
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static SpecialistTurnException Failure(string code, string message, Exception? innerException = null) => SpecialistModelRunner.Failure(code, message, innerException);
    private sealed record RequestEnvelope(string Purpose, SpecialistInvocationIdentity Identity, string Hash, SpecialistTurnRequest Request);
    private sealed record ResultEnvelope(string Purpose, SpecialistInvocationIdentity Identity, string RequestHash, string ChildInvocationId, string Fence, SpecialistTurnResult Result, int AdmissionGeneration = 0);
}
