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
        var key = Hash(JsonSerializer.Serialize(new { request.Identity.TenantId, request.Identity.PersistencePartition, request.Identity.WorkflowExecutionId, request.Identity.OperationKey }));
        var candidate = new SpecialistTurnDocument(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), key, hash, request.Identity,
            protector.Protect(JsonSerializer.Serialize(new RequestEnvelope(RequestPurpose, request.Identity, hash, request), SpecialistJson.Options)),
            now.AddMilliseconds(request.Descriptor.Limits.TimeoutMilliseconds));
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
            Hash(JsonSerializer.Serialize(envelope.Request, SpecialistJson.Options)) != document.RequestHash)
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
            envelope.ChildInvocationId != childInvocationId || envelope.Fence != document.Fence)
            throw Failure("specialist.journal.protection", "The accepted specialist result does not match this native invocation.");
        return envelope.Result;
    }

    public async ValueTask<SpecialistTurnDocument> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(identity);
        return await store.ClaimAsync(handle, identity, childInvocationId, Guid.NewGuid().ToString("N"), now, cancellationToken)
            ?? throw Failure("specialist.turn.claim", "The specialist turn is already owned, terminal, expired or unavailable; dispatch was refused.");
    }

    public ValueTask<bool> DispatchAsync(SpecialistTurnDocument document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.DispatchAsync(document.Handle, document.Identity, document.Fence!, now, cancellationToken);
    }

    public ValueTask<bool> AcceptAsync(SpecialistTurnDocument document, SpecialistTurnResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        var envelope = new ResultEnvelope(ResultPurpose, document.Identity, document.RequestHash, document.ChildInvocationId!, document.Fence!, result);
        return store.AcceptAsync(document.Handle, document.Identity, document.Fence!, protector.Protect(JsonSerializer.Serialize(envelope, SpecialistJson.Options)), now, cancellationToken);
    }

    public ValueTask FailAsync(SpecialistTurnDocument document, string code, bool unknown, CancellationToken cancellationToken)
    {
        EnsureScope(document.Identity);
        return store.FailAsync(document.Handle, document.Identity, document.Fence!, code, unknown, cancellationToken);
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
    private sealed record ResultEnvelope(string Purpose, SpecialistInvocationIdentity Identity, string RequestHash, string ChildInvocationId, string Fence, SpecialistTurnResult Result);
}
