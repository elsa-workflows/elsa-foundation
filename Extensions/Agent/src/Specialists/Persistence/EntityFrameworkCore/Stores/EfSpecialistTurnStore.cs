using static Elsa.Agent.Specialists.Core.Models.SpecialistTurnDocumentRules;
using Elsa.Agent.Specialists.Core.Exceptions;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfSpecialistTurnStore(DbContext context, IPersistenceAccessContextAccessor access) : ISpecialistTurnStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaximumRecordBytes = 64 * 1024 * 1024;
    public ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
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
                if (factory is null) return ValueTask.FromResult(new SpecialistJournalCapabilities(provider, false));
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
            return ValueTask.FromResult(new SpecialistJournalCapabilities(provider, durable));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken)
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
                    var existing = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                    if (existing is not null)
                    {
                        var before = Read(existing);
                        var next = Replay(before, candidate);
                        if (ReferenceEquals(before, next)) return Clone(before);
                        Validate(next);
                        var changed = await owned.Set<SpecialistTurnRecord>().Where(x => x.ScopeKey == scope && x.Handle == existing.Handle &&
                            x.SchemaVersion == SpecialistsEfModule.SchemaVersion && x.Version == existing.Version)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, existing.Version + 1).SetProperty(x => x.Payload, Serialize(next)), cancellationToken);
                        if (changed != 1) continue;
                        await transaction.CommitAsync(cancellationToken);
                        return Clone(next);
                    }
                    owned.Set<SpecialistTurnRecord>().Add(Row(scope, candidate));
                    await owned.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Clone(candidate);
                }
                catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
                {
                    // A confirmed competing insert has no inference effect. Register this exact
                    // matching delivery against its committed winner in a fresh metadata transaction.
                    var winner = await context.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                    if (winner is null) throw Failure(exception, candidate.Handle);
                }
            }
            throw Failure(new InvalidOperationException("The bounded specialist stage metadata retries were exhausted."), candidate.Handle);
        }
        catch (OperationCanceledException) { throw; }
        catch (SpecialistJournalException) { throw; }
        catch (Exception exception) { throw Failure(exception, candidate.Handle); }
    }

    public async ValueTask<SpecialistTurnDocument?> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            var scope = Scope(identity);
            Token(handle);
            var row = await context.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Handle == handle, cancellationToken);
            if (row is null) return null;
            var document = Read(row);
            return SpecialistTurnDeliveryRules.Admits(document, identity) ? document : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception, handle); }
    }

    public async ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childInvocationId);
        Fence(fence);
        // Expired dispatch responsibility is never taken over for another provider call.
        _ = await ChangeAsync(handle, identity, now, requireDeadline: false,
            document => document.Status == SpecialistTurnStatus.Dispatching && (document.LeaseExpiresAt is null || document.LeaseExpiresAt <= now || document.Deadline <= now),
            document => document with { Status = SpecialistTurnStatus.OutcomeUnknown, FailureCode = "specialist.turn.outcome-unknown" }, cancellationToken);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => SpecialistTurnLeaseRules.CanClaim(document, identity, childInvocationId, now),
            document => SpecialistTurnLeaseRules.Claim(document, identity, childInvocationId, fence, now), cancellationToken);
    }

    public ValueTask<SpecialistTurnDocument?> ReplayAcceptedAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken) =>
        ChangeAsync(handle, identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => document.Status == SpecialistTurnStatus.Accepted && SpecialistTurnDeliveryRules.CanBindChild(document, identity, childInvocationId),
            document => SpecialistTurnDeliveryRules.BindChild(document, identity, childInvocationId), cancellationToken, changeConversation: false);

    public ValueTask<SpecialistTurnDocument?> RenewAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching && SpecialistTurnLeaseRules.Live(document, lease, now),
            document => document with { LeaseExpiresAt = SpecialistTurnLeaseRules.Expires(document, now) is var expiry && expiry > document.LeaseExpiresAt ? expiry : document.LeaseExpiresAt }, cancellationToken);
    }

    public async ValueTask<SpecialistConversationDocument?> ReadConversationAsync(string conversationId, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            var scope = Scope(identity);
            Token(conversationId);
            var row = await context.Set<SpecialistConversationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Id == conversationId, cancellationToken);
            if (row is null) return null;
            var document = ReadConversation(row);
            return document.Owner == SpecialistConversationOwner.FromInvocation(identity) ? document : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Claimed && SpecialistTurnLeaseRules.Live(document, lease, now),
            document => document with { Status = SpecialistTurnStatus.Dispatching }, cancellationToken) is not null;
    }

    public async ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedResult);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Dispatching && SpecialistTurnLeaseRules.Live(document, lease, now) &&
                (document.ModelRequests is not { Count: > 0 } || document.ResponseObservation is not null),
            document => document with { Status = SpecialistTurnStatus.Accepted, ProtectedResult = protectedResult }, cancellationToken) is not null;
    }

    public async ValueTask<SpecialistModelRequestReservation?> ReserveModelRequestAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease,
        SpecialistModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Dispatching && SpecialistTurnLeaseRules.Live(document, lease, now) && document.ResponseObservation is null &&
                (document.ModelRequests?.Count ?? 0) < document.MaximumModelCalls && !(document.ModelRequests ?? []).Any(x => x.Id == reservation.Id),
            document => document with { ModelRequests = [.. document.ModelRequests ?? [], reservation] }, cancellationToken) is not null ? reservation : null;
    }

    public async ValueTask<bool> CompleteModelResponseAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, IReadOnlyList<string> requestIds,
        SpecialistModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentNullException.ThrowIfNull(requestIds);
        ArgumentNullException.ThrowIfNull(observation);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Dispatching && SpecialistTurnLeaseRules.Live(document, lease, now) && document.ResponseObservation is null &&
                requestIds.Count > 0 && (document.ModelRequests ?? []).Select(x => x.Id).SequenceEqual(requestIds, StringComparer.Ordinal),
            document => document with { ResponseObservation = observation }, cancellationToken) is not null;
    }

    public async ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        Lease(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _ = await ChangeAsync(handle, identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => document.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching && SpecialistTurnLeaseRules.Matches(document, lease),
            document => document with { Status = outcomeUnknown ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Failed, FailureCode = code }, cancellationToken);
    }

    private async ValueTask<SpecialistTurnDocument?> ChangeAsync(string handle, SpecialistInvocationIdentity identity, DateTimeOffset now, bool requireDeadline,
        Func<SpecialistTurnDocument, bool> admits, Func<SpecialistTurnDocument, SpecialistTurnDocument> change, CancellationToken cancellationToken, bool changeConversation = true)
    {
        try
        {
            var scope = Scope(identity);
            Token(handle);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                var owned = transaction.Context<DbContext>();
                var row = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Handle == handle, cancellationToken);
                if (row is null) return null;
                var before = Read(row);
                if (!SpecialistTurnDeliveryRules.Admits(before, identity) || !admits(before) || requireDeadline && before.Deadline <= now) return null;
                var next = change(before);
                Validate(next);
                SpecialistConversationRecord? conversationRow = null;
                SpecialistConversationDocument? conversationNext = null;
                if (changeConversation && before.Conversation is { } admission)
                {
                    conversationRow = await owned.Set<SpecialistConversationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Id == admission.ConversationId, cancellationToken);
                    conversationNext = SpecialistConversationRules.Change(before, next, conversationRow is null ? null : ReadConversation(conversationRow));
                    if (conversationNext is null) return null;
                    SpecialistConversationRules.Validate(conversationNext);
                }
                var payload = Serialize(next);
                var ticks = now.UtcTicks;
                var changed = await owned.Set<SpecialistTurnRecord>().Where(x => x.ScopeKey == scope && x.Handle == handle &&
                    x.SchemaVersion == SpecialistsEfModule.SchemaVersion && x.Version == row.Version && x.Status == row.Status && x.Fence == row.Fence &&
                    (!requireDeadline || x.DeadlineUtcTicks > ticks))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, row.Version + 1).SetProperty(x => x.Status, (int)next.Status)
                        .SetProperty(x => x.Fence, next.Fence).SetProperty(x => x.Payload, payload), cancellationToken);
                if (changed != 1) continue;
                if (conversationNext is not null)
                {
                    var conversationPayload = JsonSerializer.Serialize(conversationNext, Json);
                    if (conversationRow is null)
                    {
                        owned.Set<SpecialistConversationRecord>().Add(new() { ScopeKey = scope, Id = conversationNext.Id,
                            SchemaVersion = SpecialistsEfModule.SchemaVersion, Payload = conversationPayload });
                        await owned.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        var conversationChanged = await owned.Set<SpecialistConversationRecord>().Where(x => x.ScopeKey == scope && x.Id == conversationRow.Id &&
                            x.SchemaVersion == SpecialistsEfModule.SchemaVersion && x.Version == conversationRow.Version)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, conversationRow.Version + 1).SetProperty(x => x.Payload, conversationPayload), cancellationToken);
                        // Disposing this uncommitted transaction also rolls back the preceding turn CAS.
                        if (conversationChanged != 1) continue;
                    }
                }
                await transaction.CommitAsync(cancellationToken);
                return next;
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception, handle); }
    }

    private string Scope(SpecialistInvocationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(identity.TenantId);
        if (scope.Value != identity.PersistencePartition) throw new InvalidOperationException("The specialist operation does not belong to the selected persistence scope.");
        foreach (var value in new[] { identity.TenantId, identity.PersistencePartition, identity.WorkflowExecutionId, identity.ActivityExecutionId,
                     identity.OperationKey, identity.ArtifactId, identity.ArtifactHash, identity.DefinitionVersionId, identity.TemplateHash })
            if (value is not null) _ = Utf8.GetByteCount(value);
        return Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(JsonSerializer.Serialize(new[] { identity.PersistencePartition, identity.TenantId })))).ToLowerInvariant();
    }

    private static SpecialistTurnDocument Replay(SpecialistTurnDocument existing, SpecialistTurnDocument candidate)
    {
        if (!SpecialistTurnDeliveryRules.SameLogical(existing.Identity, candidate.Identity) || existing.Key != candidate.Key || existing.RequestHash != candidate.RequestHash ||
            existing.DeliveryProtocol != candidate.DeliveryProtocol || existing.DeliveryProtocol == 0 && existing.Identity != candidate.Identity)
            throw new SpecialistJournalException("specialist.journal.conflict", existing.Handle, "A different specialist request already owns this native operation.", new InvalidOperationException("The native operation fingerprint changed."));
        return SpecialistTurnDeliveryRules.Register(existing, candidate.Identity);
    }

    private static SpecialistTurnRecord Row(string scope, SpecialistTurnDocument document) => new()
    {
        ScopeKey = scope, Handle = document.Handle, OperationKey = document.Key,
        SchemaVersion = SpecialistsEfModule.SchemaVersion, Payload = Serialize(document),
        Status = (int)document.Status, Fence = document.Fence, DeadlineUtcTicks = document.Deadline.UtcTicks
    };

    private static SpecialistTurnDocument Read(SpecialistTurnRecord row)
    {
        if (row.SchemaVersion != SpecialistsEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The specialist journal record schema or bounds are unsupported.");
        var document = JsonSerializer.Deserialize<SpecialistTurnDocument>(row.Payload, Json) ?? throw new InvalidDataException("The specialist journal record is missing.");
        Validate(document);
        if (document.Handle != row.Handle || document.Key != row.OperationKey || (int)document.Status != row.Status || document.Fence != row.Fence || document.Deadline.UtcTicks != row.DeadlineUtcTicks)
            throw new InvalidDataException("The specialist journal header does not match its protected record.");
        return document;
    }

    private static string Serialize(SpecialistTurnDocument document)
    {
        var payload = JsonSerializer.Serialize(document, Json);
        if (payload.Length > MaximumRecordBytes || Utf8.GetByteCount(payload) > MaximumRecordBytes)
            throw new InvalidDataException("The specialist journal record exceeds the storage profile.");
        return payload;
    }

    private static SpecialistConversationDocument ReadConversation(SpecialistConversationRecord row)
    {
        if (row.SchemaVersion != SpecialistsEfModule.SchemaVersion || row.Version < 0 || row.Payload.Length > MaximumRecordBytes)
            throw new InvalidDataException("The specialist conversation record schema or bounds are unsupported.");
        var document = JsonSerializer.Deserialize<SpecialistConversationDocument>(row.Payload, Json) ?? throw new InvalidDataException("The specialist conversation record is missing.");
        SpecialistConversationRules.Validate(document);
        if (document.Id != row.Id) throw new InvalidDataException("The specialist conversation header does not match its record.");
        return document;
    }
    private static SpecialistTurnDocument Clone(SpecialistTurnDocument document) => JsonSerializer.Deserialize<SpecialistTurnDocument>(Serialize(document), Json)!;
    private static SpecialistJournalException Failure(Exception exception, string? handle = null) =>
        new("specialist.journal.store", handle,
            "The specialist journal store could not confirm its conditional operation.", exception);
}
