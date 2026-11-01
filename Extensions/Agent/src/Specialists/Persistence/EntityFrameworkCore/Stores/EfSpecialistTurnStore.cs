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
            try
            {
                await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
                var owned = transaction.Context<DbContext>();
                var existing = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                if (existing is not null) return Replay(existing, candidate);
                owned.Set<SpecialistTurnRecord>().Add(Row(scope, candidate));
                await owned.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Clone(candidate);
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            {
                // Staging has no inference effect. A concurrent insert's exact committed winner is
                // authoritative; never replace it with this caller's ciphertext, deadline or arguments.
                var winner = await context.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.OperationKey == candidate.Key, cancellationToken);
                return winner is null ? throw Failure(exception, candidate.Handle) : Replay(winner, candidate);
            }
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
            return document.Identity == identity ? document : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(exception, handle); }
    }

    public ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childInvocationId);
        Fence(fence);
        return ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Prepared,
            document => document with { Status = SpecialistTurnStatus.Claimed, ChildInvocationId = childInvocationId, Fence = fence }, cancellationToken);
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

    public async ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Fence(fence);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Claimed && document.Fence == fence,
            document => document with { Status = SpecialistTurnStatus.Dispatching }, cancellationToken) is not null;
    }

    public async ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, string fence, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Fence(fence);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedResult);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Dispatching && document.Fence == fence &&
                (document.ModelRequests is not { Count: > 0 } || document.ResponseObservation is not null),
            document => document with { Status = SpecialistTurnStatus.Accepted, ProtectedResult = protectedResult }, cancellationToken) is not null;
    }

    public async ValueTask<SpecialistModelRequestReservation?> ReserveModelRequestAsync(string handle, SpecialistInvocationIdentity identity, string fence,
        SpecialistModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Fence(fence);
        // The conditional write can lose to another reservation without an inference effect.
        // Re-read safely; the finite admission conditions are checked on every attempt.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var changed = await ChangeAsync(handle, identity, now, requireDeadline: true,
                document => document.Status == SpecialistTurnStatus.Dispatching && document.Fence == fence && document.ResponseObservation is null &&
                    (document.ModelRequests?.Count ?? 0) < document.MaximumModelCalls &&
                    !(document.ModelRequests ?? []).Any(x => x.Id == reservation.Id),
                document => document with { ModelRequests = [.. document.ModelRequests ?? [], reservation] }, cancellationToken);
            if (changed is not null) return reservation;
            var current = await ReadAsync(handle, identity, cancellationToken);
            if (current is null || current.Status != SpecialistTurnStatus.Dispatching || current.Fence != fence || current.Deadline <= now || current.ResponseObservation is not null ||
                (current.ModelRequests?.Count ?? 0) >= current.MaximumModelCalls || (current.ModelRequests ?? []).Any(x => x.Id == reservation.Id)) return null;
        }
        return null;
    }

    public async ValueTask<bool> CompleteModelResponseAsync(string handle, SpecialistInvocationIdentity identity, string fence, IReadOnlyList<string> requestIds,
        SpecialistModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Fence(fence);
        ArgumentNullException.ThrowIfNull(requestIds);
        ArgumentNullException.ThrowIfNull(observation);
        return await ChangeAsync(handle, identity, now, requireDeadline: true,
            document => document.Status == SpecialistTurnStatus.Dispatching && document.Fence == fence && document.ResponseObservation is null &&
                requestIds.Count > 0 && (document.ModelRequests ?? []).Select(x => x.Id).SequenceEqual(requestIds, StringComparer.Ordinal),
            document => document with { ResponseObservation = observation }, cancellationToken) is not null;
    }

    public async ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, string fence, string code, bool outcomeUnknown, CancellationToken cancellationToken)
    {
        Fence(fence);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _ = await ChangeAsync(handle, identity, DateTimeOffset.MinValue, requireDeadline: false,
            document => document.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching && document.Fence == fence,
            document => document with { Status = outcomeUnknown ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Failed, FailureCode = code }, cancellationToken);
    }

    private async ValueTask<SpecialistTurnDocument?> ChangeAsync(string handle, SpecialistInvocationIdentity identity, DateTimeOffset now, bool requireDeadline,
        Func<SpecialistTurnDocument, bool> admits, Func<SpecialistTurnDocument, SpecialistTurnDocument> change, CancellationToken cancellationToken)
    {
        try
        {
            var scope = Scope(identity);
            Token(handle);
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            var row = await owned.Set<SpecialistTurnRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scope && x.Handle == handle, cancellationToken);
            if (row is null) return null;
            var before = Read(row);
            if (before.Identity != identity || !admits(before) || requireDeadline && before.Deadline <= now) return null;
            var next = change(before);
            Validate(next);
            SpecialistConversationRecord? conversationRow = null;
            SpecialistConversationDocument? conversationNext = null;
            if (before.Conversation is { } admission)
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
            if (changed != 1) return null;
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
                    if (conversationChanged != 1) return null;
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return next;
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

    private static SpecialistTurnDocument Replay(SpecialistTurnRecord row, SpecialistTurnDocument candidate)
    {
        var existing = Read(row);
        if (existing.Identity != candidate.Identity || existing.Key != candidate.Key || existing.RequestHash != candidate.RequestHash)
            throw new SpecialistJournalException("specialist.journal.conflict", existing.Handle, "A different specialist request already owns this native operation.", new InvalidOperationException("The native operation fingerprint changed."));
        return existing;
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
