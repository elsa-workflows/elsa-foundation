using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acme.Processing.Core;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.AspNetCore.Http;

namespace Acme.Processing;

internal sealed class ProcessingMemoryState
{
    public ConcurrentDictionary<(string Tenant, string Id), ProcessingRecord> Records { get; } = [];
}

internal sealed class InMemoryProcessingStore(ProcessingMemoryState state) : IProcessingRecordReader, IProcessingRecordWriter
{
    public ValueTask AddAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!state.Records.TryAdd((record.TenantId, record.Id), record))
            throw new ProcessingException("conflict", "This processing identity already exists.");
        return ValueTask.CompletedTask;
    }

    public ValueTask<ProcessingRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(state.Records.GetValueOrDefault((tenantId, id)));
    }

    public ValueTask<ProcessingRecordPage> ReadPageAsync(string tenantId, int take, string? afterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (take is < 1 or > 100) throw new ProcessingException("validation", "A processing page contains between 1 and 100 records.");
        var rows = state.Records.Where(pair => pair.Key.Tenant == tenantId && (afterId is null || StringComparer.Ordinal.Compare(pair.Key.Id, afterId) > 0))
            .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal).Take(take + 1).Select(pair => pair.Value).ToArray();
        return ValueTask.FromResult(new ProcessingRecordPage(rows.Take(take).ToArray(), rows.Length > take ? rows[take - 1].Id : null));
    }
}

internal sealed class StudioProcessingActorContext(IDashboardActorContext actors) : IProcessingActorContext
{
    public async ValueTask<ProcessingActor> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var actor = await actors.GetAsync(cancellationToken);
            return new(actor.TenantId, actor.UserId);
        }
        catch (DashboardResourceException exception)
        { throw new ProcessingException(exception.Code == "unavailable" ? "unavailable" : "denied", "The current processing identity cannot be established.", exception); }
    }
}

internal sealed class ProcessingAccess(IProcessingActorContext actors, IHttpContextAccessor http, IPermissionAuthorizationService permissions)
{
    public async ValueTask<ProcessingActor> ActorAsync(CancellationToken cancellationToken)
    {
        if (http.HttpContext?.User.Identity?.IsAuthenticated != true)
            throw new ProcessingException("denied", "An authenticated processing subject and tenant are required.");
        var actor = await actors.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.TenantId) || string.IsNullOrWhiteSpace(actor.SubjectId))
            throw new ProcessingException("denied", "An authenticated processing subject and tenant are required.");
        return actor;
    }

    public async ValueTask<bool> PermissionAsync(string permission, object? resource, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);
        return (await permissions.AuthorizeAsync(new(http.HttpContext!.User, permission, actor.TenantId, resource), cancellationToken)).Succeeded;
    }

    public async ValueTask<bool> CanReadAsync(ProcessingRecord record, ProcessingActor actor, CancellationToken cancellationToken) =>
        record.TenantId == actor.TenantId && (record.OwnerId == actor.SubjectId || record.ReaderIds.Contains(actor.SubjectId)) &&
        await PermissionAsync(ProcessingPermissions.Read, record, cancellationToken);
}

internal sealed class ProcessingRecords(IProcessingRecordReader reader, IProcessingRecordWriter writer, ProcessingAccess access) : IProcessingRecords
{
    private static readonly byte[] CursorKey = RandomNumberGenerator.GetBytes(32);

    public async ValueTask<string> CreateAsync(ProcessingCreate input, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        if (!await access.PermissionAsync(ProcessingPermissions.Create, input, cancellationToken)) throw Denied();
        if (input is null || string.IsNullOrWhiteSpace(input.CustomerCategory) || input.CustomerCategory.Length > 80 ||
            input.CustomerCategory.Any(char.IsControl) || input.Status is not ("processing" or "completed" or "failed") ||
            input.CompletedAt < input.StartedAt || input.Status == "processing" && input.CompletedAt is not null ||
            input.ReaderIds?.Count > 20 || input.ReaderIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl)) == true)
            throw new ProcessingException("validation", "Supply a bounded category, supported status, consistent timestamps and canonical reader identities.");
        var id = Guid.NewGuid().ToString("N");
        await writer.AddAsync(new(id, actor.TenantId, actor.SubjectId, (input.ReaderIds ?? []).ToHashSet(StringComparer.Ordinal),
            input.CustomerCategory, input.StartedAt, input.CompletedAt, input.Status), cancellationToken);
        return id;
    }

    public async ValueTask<ProcessingRecordView?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        var record = await reader.FindAsync(actor.TenantId, id, cancellationToken);
        return record is not null && await access.CanReadAsync(record, actor, cancellationToken) ? await ViewAsync(record, cancellationToken) : null;
    }

    public async ValueTask<ProcessingRecordViewPage> ReadAsync(int take, string? cursor, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 100) throw new ProcessingException("validation", "A processing page contains between 1 and 100 records.");
        var actor = await access.ActorAsync(cancellationToken);
        string? after = null;
        if (cursor is not null)
        {
            string[] pieces;
            try
            {
                if (cursor.Length > 4096) throw new FormatException("Cursor exceeds its bound.");
                pieces = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            }
            catch (FormatException exception) { throw new ProcessingException("validation", "This processing cursor is invalid.", exception); }
            if (pieces.Length != 2 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pieces[1]), Encoding.UTF8.GetBytes(Sign(actor, pieces[0]))))
                throw new ProcessingException("validation", "This processing cursor is invalid for the current scope.");
            after = pieces[0];
        }
        var result = new List<ProcessingRecordView>();
        string? lastReturned = after;
        var examined = 0;
        var continuations = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await reader.ReadPageAsync(actor.TenantId, 100, after, cancellationToken);
            if (page.Items.Count > 100 || page.Items.Count == 0 && page.NextId is not null)
                throw new ProcessingException("source", "The processing store returned an invalid finite page.");
            foreach (var record in page.Items)
            {
                if (++examined > 100000) throw new ProcessingException("limit", "This processing read exceeds its bounded traversal.");
                if (!await access.CanReadAsync(record, actor, cancellationToken)) { after = record.Id; continue; }
                if (result.Count == take) return new(result, WriteCursor(actor, lastReturned!));
                result.Add(await ViewAsync(record, cancellationToken)); after = record.Id; lastReturned = record.Id;
            }
            if (page.NextId is null) return new(result, null);
            if (!continuations.Add(page.NextId)) throw new ProcessingException("source", "The processing page did not advance.");
            after = page.NextId;
        }
    }

    private async ValueTask<ProcessingRecordView> ViewAsync(ProcessingRecord record, CancellationToken cancellationToken)
    {
        var category = await access.PermissionAsync(ProcessingPermissions.CategoryRead, record, cancellationToken);
        return new(record.Id, category ? record.CustomerCategory : null, category, record.StartedAt, record.CompletedAt, record.Status);
    }

    private static string Sign(ProcessingActor actor, string after) => Convert.ToBase64String(HMACSHA256.HashData(CursorKey, JsonSerializer.SerializeToUtf8Bytes(new[] { actor.TenantId, actor.SubjectId, after })));
    private static string WriteCursor(ProcessingActor actor, string after) => Convert.ToBase64String(Encoding.UTF8.GetBytes(after + "|" + Sign(actor, after)));
    private static ProcessingException Denied() => new("denied", "This processing action is unavailable.");
}
