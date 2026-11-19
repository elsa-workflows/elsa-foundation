using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfRuntimeModelExecutionReader(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IRuntimeModelExecutionReader
{
    private const string CursorPurpose = "runtime.model-executions.v1";
    internal bool Uses(RuntimeDbContext candidate, IPersistenceAccessContextAccessor candidateAccess) =>
        ReferenceEquals(candidate, context) && ReferenceEquals(candidateAccess, access);

    public async ValueTask<RuntimeModelExecutionState?> FindAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, nameof(executionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(access);
        var id = CompositeId(scope, executionId);
        try
        {
            var row = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return row is null ? null : Read(row, scope, executionId);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
                                          exception is IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unavailable("reading execution", scope, executionId, null, exception);
        }
    }

    public async ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string executionId, string inputId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, nameof(executionId)); ValidateIdentity(inputId, nameof(inputId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(access);
        var id = CompositeId(scope, executionId, inputId);
        try
        {
            var row = await context.Set<RuntimeModelInputEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return row is null ? null : Read(row, scope, executionId, inputId);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
                                          exception is IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unavailable("reading input", scope, executionId, inputId, exception);
        }
    }

    public async ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(access);
        string? after = null;
        if (request.ContinuationToken is { } token)
        {
            try
            {
                var cursor = JsonSerializer.Deserialize<Cursor>(continuationCodec.Decode(CursorPurpose, token));
                if (cursor is null || cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || cursor.After != Order(cursor.ExecutionId))
                    throw new FormatException();
                after = cursor.After;
            }
            catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidDataException)
            { throw new ArgumentException("The model execution continuation does not match this scoped query.", nameof(request), exception); }
        }
        try
        {
            var rows = context.Set<RuntimeModelExecutionEntity>().AsNoTracking()
                .Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
            if (after is not null) rows = rows.Where(x => x.ExecutionIdOrderKey.CompareTo(after) > 0);
            var page = await rows.OrderBy(x => x.ExecutionIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken);
            var states = page.Take(request.Limit).Select(x => Read(x, scope, Decode(x.ExecutionId))).ToArray();
            var next = page.Length > request.Limit
                ? continuationCodec.Encode(CursorPurpose, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Cursor(1, Hash(scope), states[^1].ExecutionId, Order(states[^1].ExecutionId)))))
                : null;
            return new(request, states, next);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
                                          exception is IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unavailable("listing executions", scope, null, null, exception);
        }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(string operation, string scope, string? executionId,
        string? inputId, Exception exception) => new("runtime.model-state-unavailable",
        $"Model state {operation} failed in persistence scope '{scope}' for execution '{executionId ?? "<page>"}'" +
        (inputId is null ? "." : $", input '{inputId}'."), exception);

    internal static RuntimeModelExecutionState Read(RuntimeModelExecutionEntity row, string scope, string executionId)
    {
        var content = RuntimeModelStateEfModule.Chain.Upcast<RuntimeModelExecutionEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson));
        var state = Deserialize<RuntimeModelExecutionState>(content[nameof(row.ContentJson)]!);
        if (row.Id != CompositeId(scope, executionId) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            state.Scope.Value != scope || state.ExecutionId != executionId || state.TenantId is not null && state.TenantId != scope ||
            row.ExecutionId != Encode(executionId) || row.ExecutionIdHash != Hash(executionId) || row.ExecutionIdOrderKey != Order(executionId) ||
            row.ArtifactIdHash != Hash(state.Executable.ArtifactId) || row.Lifecycle != (int)state.Lifecycle || row.Revision != state.Revision)
            throw new InvalidDataException("The committed model execution envelope does not match its content.");
        return state;
    }

    internal static RuntimeModelInputReceipt Read(RuntimeModelInputEntity row, string scope, string executionId, string inputId)
    {
        var content = RuntimeModelStateEfModule.Chain.Upcast<RuntimeModelInputEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson));
        var receipt = Deserialize<RuntimeModelInputReceipt>(content[nameof(row.ContentJson)]!);
        if (row.Id != CompositeId(scope, executionId, inputId) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.ExecutionId != Encode(executionId) || row.ExecutionIdHash != Hash(executionId) ||
            row.InputId != Encode(inputId) || row.InputIdHash != Hash(inputId) ||
            receipt.Scope.Value != scope || receipt.ExecutionId != executionId || receipt.InputId != inputId || row.Revision != 1)
            throw new InvalidDataException("The committed model input envelope does not match its content.");
        return receipt;
    }

    private static T Deserialize<T>(string json)
    {
        try { return RuntimeArtifactJson.Deserialize<T>(json); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("Committed model state is not valid supported data.", exception); }
    }

    private sealed record Cursor(int Version, string ScopeHash, string ExecutionId, string After);
}
