using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Shared session, scope, and query plumbing for the Groundwork v2 runtime stores.</summary>
/// <remarks>
/// Every runtime store refuses global and across-scope access: each call opens a session against the current
/// persistence scope through <see cref="IGroundworkStorageSessionSource.Open"/> and never holds one across calls.
/// The base deliberately owns only what is identical across stores (access checks, unit lookup, column typing,
/// paging, and the optimistic-write outcome vocabulary); row identity, projection checks, and write semantics stay
/// with each store and its conventions.
/// </remarks>
public abstract class GroundworkV2RuntimeStoreBase
{
    private readonly StorageUnit? unit;
    private readonly string label;

    /// <param name="label">
    /// Short store description used in diagnostics, e.g. <c>"bookmark state"</c>; messages read
    /// "Groundwork {label} requires one explicit persistence scope".
    /// </param>
    /// <param name="primaryUnitKind">
    /// The manifest document kind backing <see cref="Unit"/> and <see cref="Open()"/>; <see langword="null"/> for
    /// stores that address every unit explicitly.
    /// </param>
    protected GroundworkV2RuntimeStoreBase(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName,
        string label,
        string? primaryUnitKind)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        Sessions = sessions;
        AccessContextAccessor = accessContextAccessor;
        TargetName = targetName;
        this.label = label;
        unit = primaryUnitKind is null ? null : sessions.Unit(primaryUnitKind, targetName);
    }

    protected IGroundworkStorageSessionSource Sessions { get; }

    protected IPersistenceAccessContextAccessor AccessContextAccessor { get; }

    protected string? TargetName { get; }

    /// <summary>The store's primary storage unit.</summary>
    protected StorageUnit Unit =>
        unit ?? throw new InvalidOperationException($"Groundwork {label} store does not declare a primary unit.");

    protected StorageUnit UnitFor(string kind) => Sessions.Unit(kind, TargetName);

    protected PersistenceAccessContext AccessContext =>
        AccessContextAccessor.Current ??
        throw new InvalidOperationException($"Groundwork {label} persistence access context is missing.");

    /// <summary>Returns the current access context, refusing global and across-scope access.</summary>
    protected PersistenceAccessContext RequireScopedContext()
    {
        var context = AccessContext;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                $"Groundwork {label} requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return context;
    }

    protected StorageAccess ScopedAccess => StorageAccess.Scoped(new StorageScope(RequireScopedContext().Scope!.Value));

    protected void EnsureTenant(string? tenantId) => AccessContext.EnsureTenantScope(tenantId);

    protected IStorageSession Open() => OpenScoped(Unit);

    protected IStorageSession OpenScoped(StorageUnit storageUnit) =>
        Sessions.Open(storageUnit.Id.Value, ScopedAccess, TargetName);

    protected IUnitOfWork BeginAtomicUnitOfWork(IReadOnlyList<string> unitIds) =>
        Sessions.BeginUnitOfWork(ScopedAccess, BatchWriteOptions.Exact, unitIds, TargetName);

    /// <summary>Whether the selected provider evidences the atomic-commit capability required for multi-unit writes.</summary>
    protected bool HasAtomicCommit =>
        Sessions is IGroundworkStorageCapabilitySource capabilitySource &&
        capabilitySource.Capabilities(TargetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit));

    /// <summary>Upserts <paramref name="values"/> only if the row still carries <paramref name="revision"/>.</summary>
    protected WriteOutcome ConditionalUpsert(IStorageSession session, StorageValues values, long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                $"The selected Groundwork provider does not advertise optimistic {label} concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    protected ColumnRef Column(TableId table, string name) => Column(Unit, table, name);

    protected ColumnRef Column(StorageUnit storageUnit, TableId table, string name)
    {
        var definition = storageUnit.Columns.SingleOrDefault(column => StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork {label} unit '{storageUnit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork {label} query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    protected static Predicate Equal(ColumnRef column, object? value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    protected static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    protected static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    protected static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
