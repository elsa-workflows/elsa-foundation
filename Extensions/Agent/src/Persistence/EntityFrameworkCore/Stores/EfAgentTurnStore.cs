using static Elsa.Agent.Core.Models.AgentTurnDocumentRules;
using Elsa.Agent.Core.Exceptions;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfAgentTurnStore(DbContext context, IPersistenceAccessContextAccessor access) : IAgentTurnStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaximumRecordBytes = 64 * 1024 * 1024;
    public ValueTask<AgentJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var provider = context.Database.ProviderName ?? "unconfigured";
            var durable = provider is EfProviderNames.SqlServer or EfProviderNames.PostgreSql or EfProviderNames.MySql;
            if (provider == EfProviderNames.Sqlite)
            {
                var connection = context.Database.GetDbConnection();
                var factory = DbProviderFactories.GetFactory(connection);
                if (factory is null) return ValueTask.FromResult(new AgentJournalCapabilities(provider, false));
                var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
                builder.ConnectionString = connection.ConnectionString;
                var source = new[] { "Data Source", "DataSource", "Filename" }.Select(key => builder.TryGetValue(key, out var value) ? value?.ToString() : null)
                    .FirstOrDefault(value => value is not null) ?? connection.DataSource;
                var memory = builder.TryGetValue("Mode", out var mode) && string.Equals(mode?.ToString(), "Memory", StringComparison.OrdinalIgnoreCase);
                var path = source;
                if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    // SQLite decodes percent escapes in URI keys/values and discards fragments.
                    // An omitted path is a temporary database, even without mode=memory.
                    var uri = source[5..].Split('#', 2)[0];
                    var parts = uri.Split('?', 2);
                    path = parts[0];
                    if (path.StartsWith("//", StringComparison.Ordinal))
                    {
                        var slash = path.IndexOf('/', 2);
                        path = slash < 0 ? "" : path[slash..];
                    }
                    path = Uri.UnescapeDataString(path);
                    if (parts.Length == 2)
                        foreach (var parameter in parts[1].Split('&'))
                        {
                            var pair = parameter.Split('=', 2);
                            var key = Uri.UnescapeDataString(pair[0]);
                            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
                            memory |= key.Equals("mode", StringComparison.OrdinalIgnoreCase) && value.Equals("memory", StringComparison.OrdinalIgnoreCase);
                            // Custom VFS persistence is not established by this provider profile.
                            memory |= key.Equals("vfs", StringComparison.OrdinalIgnoreCase) || key.Contains('\0') || value.Contains('\0');
                        }
                }
                memory |= path.Equals(":memory:", StringComparison.OrdinalIgnoreCase) || path.Contains('\0');
                durable = !string.IsNullOrWhiteSpace(path) && !memory;
            }
            return ValueTask.FromResult(new AgentJournalCapabilities(provider, durable));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, CancellationToken cancellationToken) => StageAsync(candidate, true, cancellationToken);

    public async ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, bool allowNewOperation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        try
        {
            var scope = Scope(candidate.Identity);
            Prepared(candidate);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                try
                {
                    await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                    var owned = transaction.Context<DbContext>();
                    var operation = await LockOperationAsync(owned, scope, candidate.Identity, cancellationToken);
                    if (operation is null) continue;
                    if (AgentOperationRules.Cancelled(operation.Value.Document, candidate.Identity))
                        throw new AgentJournalException("agent.journal.cancelled", null, "This native parent was cancelled before admission.", new InvalidOperationException("A retained cancellation blocks this delivery."));
                    var existing = await owned.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                    if (existing is not null)
                    {
                        var before = Read(existing);
                        var next = Replay(before, candidate);
                        if (ReferenceEquals(before, next)) { await transaction.CommitAsync(cancellationToken); return Clone(before); }
                        Validate(next);
                        var changed = await owned.Set<AgentTurnRecord>().Where(x => x.ScopeKey == scope && x.Handle == existing.Handle &&
                            x.SchemaVersion == AgentEfModule.SchemaVersion && x.Version == existing.Version)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, existing.Version + 1).SetProperty(x => x.Payload, Serialize(next)), cancellationToken);
                        if (changed != 1) continue;
                        await transaction.CommitAsync(cancellationToken);
                        return Clone(next);
                    }
                    if (!allowNewOperation)
                        throw new AgentJournalException("agent.journal.admission-disabled", null, "New agent operations are disabled for this host.", new InvalidOperationException("The creation policy permits only retained operations."));
                    owned.Set<AgentTurnRecord>().Add(Row(scope, candidate));
                    await owned.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Clone(candidate);
                }
                catch (OperationGateRaceException) { /* A confirmed competing gate insert; reopen in a fresh transaction. */ }
                catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
                {
                    // A confirmed competing insert has no inference effect. Register this exact
                    // matching delivery against its committed winner in a fresh metadata transaction.
                    var winner = await context.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                    if (winner is null) throw Failure(exception, candidate.Handle);
                }
            }
            throw Failure(new InvalidOperationException("The bounded agent stage metadata retries were exhausted."), candidate.Handle);
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception, candidate.Handle); }
    }

    public async ValueTask<AgentTurnDocument?> ReadAsync(string handle, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            var scope = Scope(identity);
            Token(handle);
            var row = await context.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Handle == handle, cancellationToken);
            if (row is null) return null;
            var document = Read(row);
            return AgentTurnDeliveryRules.Admits(document, identity) ? document : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception, handle); }
    }

    public async ValueTask<AgentTurnDocument?> ClaimAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childInvocationId);
        Fence(fence);
        // Expired dispatch responsibility is never taken over for another provider call.
        _ = await ChangeAsync(handle, identity, now, requireDeadline: false,
            document => document.Status == AgentTurnStatus.Dispatching && (document.LeaseExpiresAt is null || document.LeaseExpiresAt <= now || document.Deadline <= now),
            document => document with { Status = AgentTurnStatus.OutcomeUnknown, FailureCode = "agent.turn.outcome-unknown" }, cancellationToken);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => AgentTurnLeaseRules.CanClaim(document, identity, childInvocationId, now),
            document => AgentTurnLeaseRules.Claim(document, identity, childInvocationId, fence, now), cancellationToken, claimOperation: true);
    }

    public ValueTask<AgentTurnDocument?> ReplayAcceptedAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken) =>
        ChangeAsync(handle, identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => document.Status == AgentTurnStatus.Accepted && AgentTurnDeliveryRules.CanBindChild(document, identity, childInvocationId),
            document => AgentTurnDeliveryRules.BindChild(document, identity, childInvocationId), cancellationToken, changeConversation: false);

    public ValueTask<AgentTurnDocument?> ApplyAsync(string handle, AgentTurnApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ChangeAsync(handle, application.Identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => AgentTurnApplicationRules.Admits(document, application),
            document => AgentTurnApplicationRules.Apply(document, application), cancellationToken, changeConversation: false);
    }

    public ValueTask<AgentTurnDocument?> RenewAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status is AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching && AgentTurnLeaseRules.Live(document, lease, now),
            document => document with { LeaseExpiresAt = AgentTurnLeaseRules.Expires(document, now) is var expiry && expiry > document.LeaseExpiresAt ? expiry : document.LeaseExpiresAt }, cancellationToken);
    }

    public async ValueTask<AgentConversationDocument?> ReadConversationAsync(string conversationId, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            var scope = Scope(identity);
            Token(conversationId);
            var row = await context.Set<AgentConversationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Id == conversationId, cancellationToken);
            if (row is null) return null;
            var document = ReadConversation(row);
            return document.Owner == AgentConversationOwner.FromInvocation(identity) ? document : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<bool> DispatchAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == AgentTurnStatus.Claimed && AgentTurnLeaseRules.Live(document, lease, now),
            document => document with { Status = AgentTurnStatus.Dispatching }, cancellationToken) is not null;
    }

    public async ValueTask<bool> AcceptAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedResult);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == AgentTurnStatus.Dispatching && AgentTurnLeaseRules.Live(document, lease, now) &&
                (document.ModelRequests is not { Count: > 0 } || document.ResponseObservation is not null),
            document => document with { Status = AgentTurnStatus.Accepted, ProtectedResult = protectedResult }, cancellationToken) is not null;
    }

    public async ValueTask<AgentModelRequestReservation?> ReserveModelRequestAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease,
        AgentModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == AgentTurnStatus.Dispatching && AgentTurnLeaseRules.Live(document, lease, now) && document.ResponseObservation is null &&
                (document.ModelRequests?.Count ?? 0) < document.MaximumModelCalls && !(document.ModelRequests ?? []).Any(x => x.Id == reservation.Id),
            document => document with { ModelRequests = [.. document.ModelRequests ?? [], reservation] }, cancellationToken) is not null ? reservation : null;
    }

    public async ValueTask<bool> CompleteModelResponseAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, IReadOnlyList<string> requestIds,
        AgentModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentNullException.ThrowIfNull(requestIds);
        ArgumentNullException.ThrowIfNull(observation);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == AgentTurnStatus.Dispatching && AgentTurnLeaseRules.Live(document, lease, now) && document.ResponseObservation is null &&
                requestIds.Count > 0 && (document.ModelRequests ?? []).Select(x => x.Id).SequenceEqual(requestIds, StringComparer.Ordinal),
            document => document with { ResponseObservation = observation }, cancellationToken) is not null;
    }

    public async ValueTask FailAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _ = await ChangeAsync(handle, identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => document.Status is AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching && AgentTurnLeaseRules.Matches(document, lease),
            document => document with { Status = outcomeUnknown ? AgentTurnStatus.OutcomeUnknown : AgentTurnStatus.Failed, FailureCode = code }, cancellationToken);
    }

    private async ValueTask<AgentTurnDocument?> ChangeAsync(string handle, AgentInvocationIdentity identity, DateTimeOffset now, bool requireDeadline,
        Func<AgentTurnDocument, bool> admits, Func<AgentTurnDocument, AgentTurnDocument> change, CancellationToken cancellationToken, bool changeConversation = true, bool claimOperation = false)
    {
        try
        {
            var scope = Scope(identity);
            Token(handle);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                try
                {
                    await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                    var owned = transaction.Context<DbContext>();
                    if (claimOperation)
                    {
                        var operation = await LockOperationAsync(owned, scope, identity, cancellationToken);
                        if (operation is null) continue;
                        if (AgentOperationRules.Cancelled(operation.Value.Document, identity)) return null;
                    }
                    var row = await owned.Set<AgentTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Handle == handle, cancellationToken);
                    if (row is null) return null;
                    var before = Read(row);
                    if (!AgentTurnDeliveryRules.Admits(before, identity) || !admits(before) || requireDeadline && before.Deadline <= now) return null;
                    var next = change(before);
                    Validate(next);
                    AgentConversationRecord? conversationRow = null;
                    AgentConversationDocument? conversationNext = null;
                    if (changeConversation && before.Conversation is { } admission)
                    {
                        conversationRow = await owned.Set<AgentConversationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Id == admission.ConversationId, cancellationToken);
                        conversationNext = AgentConversationRules.Change(before, next, conversationRow is null ? null : ReadConversation(conversationRow));
                        if (conversationNext is null) return null;
                        AgentConversationRules.Validate(conversationNext);
                    }
                    var payload = Serialize(next);
                    var ticks = now.UtcTicks;
                    var changed = await owned.Set<AgentTurnRecord>().Where(x => x.ScopeKey == scope && x.Handle == handle &&
                        x.SchemaVersion == AgentEfModule.SchemaVersion && x.Version == row.Version && x.Status == row.Status && x.Fence == row.Fence &&
                        (!requireDeadline || x.DeadlineUtcTicks > ticks))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, row.Version + 1).SetProperty(x => x.Status, (int)next.Status)
                            .SetProperty(x => x.Fence, next.Fence).SetProperty(x => x.Payload, payload), cancellationToken);
                    if (changed != 1) continue;
                    if (conversationNext is not null)
                    {
                        var conversationPayload = JsonSerializer.Serialize(conversationNext, Json);
                        if (conversationRow is null)
                        {
                            owned.Set<AgentConversationRecord>().Add(new() { ScopeKey = scope, Id = conversationNext.Id,
                                SchemaVersion = AgentEfModule.SchemaVersion, Payload = conversationPayload });
                            await owned.SaveChangesAsync(cancellationToken);
                        }
                        else
                        {
                            var conversationChanged = await owned.Set<AgentConversationRecord>().Where(x => x.ScopeKey == scope && x.Id == conversationRow.Id &&
                                x.SchemaVersion == AgentEfModule.SchemaVersion && x.Version == conversationRow.Version)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, conversationRow.Version + 1).SetProperty(x => x.Payload, conversationPayload), cancellationToken);
                            // Disposing this uncommitted transaction also rolls back the preceding turn CAS.
                            if (conversationChanged != 1) continue;
                        }
                    }
                    await transaction.CommitAsync(cancellationToken);
                    return next;
                }
                catch (OperationGateRaceException) { /* A confirmed competing gate insert; retry only this metadata admission. */ }
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (AgentJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception, handle); }
    }

    private string Scope(AgentInvocationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (var value in new[] { identity.TenantId, identity.PersistencePartition, identity.WorkflowExecutionId, identity.ActivityExecutionId,
                     identity.OperationKey, identity.ArtifactId, identity.ArtifactHash, identity.DefinitionVersionId, identity.TemplateHash })
            if (value is not null) _ = Utf8.GetByteCount(value);
        return Scope(identity.TenantId, identity.PersistencePartition);
    }

    private string Scope(string? tenantId, string persistencePartition)
    {
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(tenantId);
        if (scope.Value != persistencePartition) throw new InvalidOperationException("The agent operation does not belong to the selected persistence scope.");
        return Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(JsonSerializer.Serialize(new[] { persistencePartition, tenantId })))).ToLowerInvariant();
    }

    private static AgentTurnDocument Replay(AgentTurnDocument existing, AgentTurnDocument candidate)
    {
        if (!AgentTurnDeliveryRules.SameLogical(existing.Identity, candidate.Identity) || existing.Key != candidate.Key || existing.RequestHash != candidate.RequestHash ||
            existing.DeliveryProtocol != candidate.DeliveryProtocol || existing.DeliveryProtocol == 0 && existing.Identity != candidate.Identity)
            throw new AgentJournalException("agent.journal.conflict", existing.Handle, "A different agent request already owns this native operation.", new InvalidOperationException("The native operation fingerprint changed."));
        return AgentTurnDeliveryRules.Register(existing, candidate.Identity);
    }

    private static AgentTurnRecord Row(string scope, AgentTurnDocument document) => new()
    {
        ScopeKey = scope, Handle = document.Handle, OperationKey = document.Key,
        SchemaVersion = AgentEfModule.SchemaVersion, Payload = Serialize(document),
        Status = (int)document.Status, Fence = document.Fence, DeadlineUtcTicks = document.Deadline.UtcTicks
    };

    private static AgentTurnDocument Read(AgentTurnRecord row)
    {
        if (row.SchemaVersion != AgentEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The agent journal record schema or bounds are unsupported.");
        var document = JsonSerializer.Deserialize<AgentTurnDocument>(row.Payload, Json) ?? throw new InvalidDataException("The agent journal record is missing.");
        Validate(document);
        if (document.Handle != row.Handle || document.Key != row.OperationKey || (int)document.Status != row.Status || document.Fence != row.Fence || document.Deadline.UtcTicks != row.DeadlineUtcTicks)
            throw new InvalidDataException("The agent journal header does not match its protected record.");
        return document;
    }

    private static string Serialize(AgentTurnDocument document)
    {
        var payload = JsonSerializer.Serialize(document, Json);
        if (payload.Length > MaximumRecordBytes || Utf8.GetByteCount(payload) > MaximumRecordBytes)
            throw new InvalidDataException("The agent journal record exceeds the storage profile.");
        return payload;
    }

    private static AgentConversationDocument ReadConversation(AgentConversationRecord row)
    {
        if (row.SchemaVersion != AgentEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The agent conversation record schema or bounds are unsupported.");
        var document = JsonSerializer.Deserialize<AgentConversationDocument>(row.Payload, Json) ?? throw new InvalidDataException("The agent conversation record is missing.");
        AgentConversationRules.Validate(document);
        if (document.Id != row.Id) throw new InvalidDataException("The agent conversation header does not match its record.");
        return document;
    }
    private static AgentTurnDocument Clone(AgentTurnDocument document) => JsonSerializer.Deserialize<AgentTurnDocument>(Serialize(document), Json)!;
    private static AgentJournalException Failure(Exception exception, string? handle = null) =>
        new("agent.journal.store", handle,
            "The agent journal store could not confirm its conditional operation.", exception);
}
