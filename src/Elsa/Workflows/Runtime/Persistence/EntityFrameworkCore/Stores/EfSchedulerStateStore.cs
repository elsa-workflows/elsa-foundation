using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core scheduler-state store (R15).</summary>
public sealed class EfSchedulerStateStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : ISchedulerStateStore
{
    private const string Collection = "schedulerState";

    public async ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId);
        context.ChangeTracker.Clear();
        var row = await context.SchedulerStates.SingleOrDefaultAsync(x =>
            x.Id == id &&
            x.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            x.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            x.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) &&
            x.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId),
            cancellationToken);
        if (row is null)
            context.SchedulerStates.Add(ToEntity(state, scope, id, 1));
        else
        {
            _ = Read(row, scope, state.WorkflowExecutionId);
            Copy(row, state, scope, checked(row.Revision + 1));
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The scheduler state changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The scheduler state changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await context.SchedulerStates.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId) &&
            x.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            x.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            x.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            x.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId),
            cancellationToken);
        return row is null ? null : Read(row, scope, workflowExecutionId);
    }

    public async ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var result = new List<SchedulerState>();
        string? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = context.SchedulerStates.AsNoTracking().Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey);
            if (last is not null)
                query = query.Where(x => string.Compare(x.WorkflowExecutionIdOrderKey, last) > 0);
            var rows = await query.OrderBy(x => x.WorkflowExecutionIdOrderKey).Take(RuntimeStorePageRequest.MaximumLimit + 1).ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > RuntimeStorePageRequest.MaximumLimit;
            if (hasNext) rows = rows[..RuntimeStorePageRequest.MaximumLimit];
            result.AddRange(rows.Select(row => Read(row, scope)));
            if (!hasNext) return result;
            last = rows[^1].WorkflowExecutionIdOrderKey;
        }
    }

    // Internal checkpoint staging reuses the direct store's provider-neutral projection.
    internal static SchedulerStateEntity ToEntity(SchedulerState state, string scope, string id, long revision) => new()
    {
        Id = id, ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId), WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId), WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId), Collection = Collection,
        ContentJson = EfSchedulerStateJson.Serialize(state), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = revision
    };

    internal static void Copy(SchedulerStateEntity row, SchedulerState state, string scope, long revision)
    {
        var replacement = ToEntity(state, scope, row.Id, revision);
        row.ScopeKey = replacement.ScopeKey; row.ScopeKeyHash = replacement.ScopeKeyHash; row.WorkflowExecutionId = replacement.WorkflowExecutionId; row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash; row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey; row.Collection = replacement.Collection; row.ContentJson = replacement.ContentJson; row.SchemaVersion = replacement.SchemaVersion; row.Revision = revision;
    }

    internal static SchedulerState Read(SchedulerStateEntity row, string scope, string? expectedWorkflow = null)
    {
        if (row.Revision <= 0 ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope) ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope))
            throw new InvalidDataException("The scheduler-state row scope or revision projection is corrupt.");
        var state = EfSchedulerStateJson.Deserialize(row.ContentJson);
        if ((expectedWorkflow is not null && !StringComparer.Ordinal.Equals(expectedWorkflow, state.WorkflowExecutionId)) ||
            row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId) ||
            row.Collection != Collection ||
            row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion)
            throw new InvalidDataException("The scheduler-state row identity or projection is corrupt.");
        return state;
    }

}

internal static class EfSchedulerStateJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(SchedulerState state) => JsonSerializer.Serialize(state, Options);
    public static SchedulerState Deserialize(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var workflowExecutionId = root.GetProperty("workflowExecutionId").GetString();
        if (string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new InvalidDataException("The scheduler-state content does not contain a workflow execution ID.");
        return new SchedulerState(
            workflowExecutionId,
            root.GetProperty("version").GetInt64(),
            Read<ScheduledActivityWorkItem>(root, "pendingWork"),
            Read<SchedulerContinuationWorkItem>(root, "pendingContinuations"),
            Read<VolatileWaitRegistration>(root, "volatileWaits"),
            Read<SchedulerCompletionWorkItem>(root, "pendingCompletionWork"),
            Read<GeneratorRegistration>(root, "activeGenerators"),
            Read<SchedulerGeneratedEventWorkItem>(root, "pendingGeneratedEvents"));
    }

    private static IReadOnlyCollection<T> Read<T>(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"The scheduler-state content does not contain array '{property}'.");
        return JsonSerializer.Deserialize<T[]>(value.GetRawText(), Options)
               ?? throw new InvalidDataException($"The scheduler-state array '{property}' was empty or invalid.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
