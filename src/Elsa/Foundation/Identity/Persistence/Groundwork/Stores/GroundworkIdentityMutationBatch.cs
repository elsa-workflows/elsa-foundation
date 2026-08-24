using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Identity-owned read/stage context over ordinary v2 rows. Reads occur before the provider UOW opens;
/// the final coalesced row set retains the first observed version as its provider CAS.
/// </summary>
public sealed class GroundworkIdentityMutationBatch(GroundworkIdentityRowStore rows)
{
    private readonly Dictionary<RowIdentity, RowState> states = [];
    private readonly List<RowIdentity> order = [];

    public GroundworkIdentityRow? Read(
        string unitId,
        string id,
        CancellationToken cancellationToken = default) =>
        State(unitId, id, cancellationToken).Current;

    public Task<GroundworkIdentityRow?> ReadAsync(
        string unitId,
        string id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(unitId, id, cancellationToken));

    public GroundworkIdentityWriteResult Save(
        GroundworkIdentityRowWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();
        var state = State(write.UnitId, write.Id, cancellationToken);
        if (Refusal(write.Condition, state.Current) is { } refusal)
            return refusal;

        var version = checked((state.Current?.Version ?? 0) + 1);
        state.Current = new GroundworkIdentityRow(
            write.UnitId,
            write.Id,
            IdentityStorageManifest.SchemaVersion,
            version,
            write.CanonicalJson,
            write.ProjectedValues);
        state.Changed = true;
        return new GroundworkIdentityWriteResult(
            state.Original is null ? WriteOutcomeStatus.Inserted : WriteOutcomeStatus.Updated,
            version,
            "Identity row staged.",
            state.Current,
            write.Id);
    }

    public GroundworkIdentityWriteResult Delete(
        GroundworkIdentityRowDelete delete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delete);
        cancellationToken.ThrowIfCancellationRequested();
        var state = State(delete.UnitId, delete.Id, cancellationToken);
        if (Refusal(delete.Condition, state.Current) is { } refusal)
            return refusal;
        if (state.Current is null)
            return new GroundworkIdentityWriteResult(WriteOutcomeStatus.NotFound, null, "Identity row was not found.");

        var deletedVersion = state.Current.Version;
        state.Current = null;
        state.Changed = true;
        return new GroundworkIdentityWriteResult(WriteOutcomeStatus.Deleted, deletedVersion, "Identity row staged for deletion.");
    }

    public BatchWriteReport Commit(CancellationToken cancellationToken = default)
    {
        var mutations = BuildMutations();
        return mutations.Count == 0
            ? new BatchWriteReport([])
            : rows.WriteBatch(mutations, cancellationToken);
    }

    internal IReadOnlyList<GroundworkIdentityRowMutation> BuildMutations()
    {
        var mutations = new List<GroundworkIdentityRowMutation>();
        foreach (var identity in order)
        {
            var state = states[identity];
            if (!state.Changed || Same(state.Original, state.Current))
                continue;

            if (state.Current is null)
            {
                if (state.Original is not null)
                {
                    mutations.Add(GroundworkIdentityRowMutation.Delete(new GroundworkIdentityRowDelete(
                        identity.UnitId,
                        identity.Id,
                        GroundworkIdentityRowWriteCondition.IfVersion(state.Original.Version))));
                }

                continue;
            }

            mutations.Add(GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
                identity.UnitId,
                identity.Id,
                state.Current.CanonicalJson,
                state.Current.ProjectedValues,
                state.Original is null
                    ? GroundworkIdentityRowWriteCondition.CreateOnly
                    : GroundworkIdentityRowWriteCondition.IfVersion(state.Original.Version))));
        }

        return mutations;
    }

    private RowState State(string unitId, string id, CancellationToken cancellationToken)
    {
        var identity = new RowIdentity(unitId, id);
        if (states.TryGetValue(identity, out var state))
            return state;
        var row = rows.Read(unitId, id, cancellationToken);
        state = new RowState(row);
        states.Add(identity, state);
        order.Add(identity);
        return state;
    }

    private static GroundworkIdentityWriteResult? Refusal(
        GroundworkIdentityRowWriteCondition condition,
        GroundworkIdentityRow? current) => condition.Kind switch
    {
        GroundworkIdentityRowWriteConditionKind.CreateOnly when current is not null =>
            new GroundworkIdentityWriteResult(WriteOutcomeStatus.ConcurrencyConflict, current.Version, "Identity row already exists."),
        GroundworkIdentityRowWriteConditionKind.ExpectedVersion when current is null =>
            new GroundworkIdentityWriteResult(WriteOutcomeStatus.NotFound, null, "Identity row was not found."),
        GroundworkIdentityRowWriteConditionKind.ExpectedVersion when current!.Version != condition.ExpectedVersion =>
            new GroundworkIdentityWriteResult(WriteOutcomeStatus.ConcurrencyConflict, current.Version, "Identity row version did not match."),
        _ => null
    };

    private static bool Same(GroundworkIdentityRow? left, GroundworkIdentityRow? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null && left == right;

    private sealed class RowState(GroundworkIdentityRow? original)
    {
        public GroundworkIdentityRow? Original { get; } = original;
        public GroundworkIdentityRow? Current { get; set; } = original;
        public bool Changed { get; set; }
    }

    private sealed record RowIdentity(string UnitId, string Id);
}
