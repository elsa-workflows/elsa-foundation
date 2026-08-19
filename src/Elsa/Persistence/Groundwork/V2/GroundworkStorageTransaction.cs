using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Opens one exact transaction over declared v2 storage units, which may belong to different feature
/// lanes.
/// <para>
/// A lane's own storage opens transactions over its own units and needs nothing from here. This exists
/// for the few operations that are genuinely one act across lanes — publishing an activity writes design
/// rows, runtime execution material and a publication receipt, and either all of it happened or none of
/// it did. In v2 every lane stages the same <see cref="RowWrite"/> primitive into the same unit of work,
/// so spanning them is the provider's ordinary transaction rather than a distributed one.
/// </para>
/// <para>
/// The units must therefore share a target. A host that splits its lanes across databases is refused
/// here, before any provider is acquired, rather than discovering mid-commit that the atomicity the
/// caller asked for cannot be honoured.
/// </para>
/// </summary>
public sealed class GroundworkStorageTransactionFactory(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor)
{
    public GroundworkStorageTransaction Begin(
        string featureIdentity,
        IReadOnlyCollection<string> unitIds,
        string? targetName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureIdentity);
        ArgumentNullException.ThrowIfNull(unitIds);
        if (unitIds.Count == 0)
            throw new ArgumentException("At least one storage unit is required.", nameof(unitIds));

        var current = accessContextAccessor.Current ?? throw new InvalidOperationException(
            $"The '{featureIdentity}' persistence access context is missing.");
        if (current.AcrossScopes || current.AccessPolicy == PersistenceAccessPolicy.Privileged)
            throw new InvalidOperationException(
                $"Privileged or across-scope '{featureIdentity}' writes are refused before provider acquisition.");
        RequireAtomicCommit(featureIdentity, targetName);

        var distinct = unitIds.Distinct(StringComparer.Ordinal).ToArray();
        var units = distinct.Select(unitId => Require(featureIdentity, unitId, targetName)).ToArray();
        var accesses = units
            .Select(unit => GroundworkStorageAccessMapper.Map(current, unit.Scope, featureIdentity))
            .Distinct()
            .ToArray();
        if (accesses.Length != 1)
            throw new InvalidOperationException(
                $"A '{featureIdentity}' transaction must use one exact persistence access context.");

        return new GroundworkStorageTransaction(
            sessions.BeginUnitOfWork(accesses[0], BatchWriteOptions.Exact, distinct, targetName),
            units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Resolves a unit on the transaction's target, reporting the lane split rather than the raw lookup
    /// failure: an operation spanning lanes reads as a missing unit when a host has simply put that lane
    /// in another database, which is a composition decision, not a schema one.
    /// </summary>
    private StorageUnit Require(string featureIdentity, string unitId, string? targetName)
    {
        try
        {
            return sessions.Unit(unitId, targetName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The '{featureIdentity}' transaction spans storage unit '{unitId}', which is not declared on " +
                $"target '{GroundworkTargetNames.Normalize(targetName)}'. This operation commits every unit " +
                "together, so its lanes must share one target; bind them to the same target or disable it.",
                exception);
        }
    }

    private void RequireAtomicCommit(string featureIdentity, string? targetName)
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                $"A '{featureIdentity}' transaction requires the provider's evidenced atomic-commit capability.");
        }
    }
}

/// <summary>One exact transaction over units the caller named. Staging is conditional, so a lost CAS race fails the commit.</summary>
public sealed class GroundworkStorageTransaction(IUnitOfWork inner, IReadOnlyDictionary<string, StorageUnit> units) : IDisposable
{
    public IReadOnlyCollection<string> UnitIds => units.Keys.ToArray();

    /// <summary>
    /// The underlying transaction, so a lane can stage through its own writer — the activity-design lane
    /// projects a request into row values one way, the runtime lane another — while every write still
    /// lands in this one transaction.
    /// <para>
    /// Exactly one participant commits. A lane writer handed this owns the commit for the whole
    /// transaction, not just its own rows.
    /// </para>
    /// </summary>
    public IUnitOfWork Inner => inner;

    /// <summary>The admitted units, for a lane writer that needs its own subset.</summary>
    public IReadOnlyDictionary<string, StorageUnit> Units => units;

    public void StageUpsert(string unitId, StorageValues values, WriteOptions options) =>
        inner.Stage(RowWrite.Upsert(Require(unitId), values, options));

    public void Stage(string unitId, StorageValues values, WriteOptions options) =>
        inner.Stage(RowWrite.ConditionalUpsert(Require(unitId), values, options));

    public void StageInsert(string unitId, StorageValues values, WriteOptions options) =>
        inner.Stage(RowWrite.Insert(Require(unitId), values, options));

    public void StageDelete(string unitId, StorageKey key, WriteOptions options) =>
        inner.Stage(RowWrite.Delete(Require(unitId), key, options));

    public BatchWriteReport Commit() => inner.CommitWithOutcomes();

    public void Rollback() => inner.Rollback();

    public void Dispose() => inner.Dispose();

    private StorageUnit Require(string unitId) => units.TryGetValue(unitId, out var unit)
        ? unit
        : throw new InvalidOperationException($"Unit '{unitId}' was not admitted to this transaction.");
}
