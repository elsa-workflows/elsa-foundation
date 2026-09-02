using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 workflow trigger-binding index.</summary>
/// <remarks>
/// This adapter is intentionally unregistered. It proves the clean-break v2 row contract while the
/// existing trigger index remains the serving implementation. Activation preparation, activation,
/// and cleanup use one provider unit of work over the binding and projection-state units; no document
/// bridge, migration path, or unconditional write fallback is involved.
/// </remarks>
public sealed class GroundworkV2WorkflowTriggerBindingStore : IWorkflowTriggerBindingStore
{
    private const string ProjectionKind = "triggerBindings";

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit bindingUnit;
    private readonly StorageUnit projectionStateUnit;

    public GroundworkV2WorkflowTriggerBindingStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        bindingUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind, targetName);
        projectionStateUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind, targetName);
    }

    public ValueTask<WorkflowTriggerBinding> SaveAsync(
        WorkflowTriggerBinding binding,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2WorkflowTriggerBindingStorageConventions.Validate(binding);
        cancellationToken.ThrowIfCancellationRequested();

        var session = OpenBindingSession();
        var key = GroundworkRuntimeRowStore.Key(binding.TriggerBindingId);
        var values = GroundworkV2WorkflowTriggerBindingStorageConventions.Values(binding);
        var existing = session.Read(key);
        var result = existing is null
            ? session.Insert(values, WriteOptions.CreateOnly)
            : ConditionalUpsert(session, values, existing, binding);
        if (IsSaved(result.Status))
            return ValueTask.FromResult(binding);
        if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected workflow trigger binding '{binding.TriggerBindingId}' with status '{result.Status}'.");
        }

        var winner = session.Read(key);
        if (winner is not null)
        {
            var winnerBinding = GroundworkV2WorkflowTriggerBindingStorageConventions.Deserialize(winner.Values.Values);
            if (StringComparer.Ordinal.Equals(
                    GroundworkV2RuntimeJson.Serialize(winnerBinding),
                    GroundworkV2RuntimeJson.Serialize(binding)))
            {
                return ValueTask.FromResult(binding);
            }
        }

        throw new InvalidOperationException(
            $"Workflow trigger binding '{binding.TriggerBindingId}' changed concurrently and was not overwritten.");
    }

    public async ValueTask PrepareActivationAsync(
        string activationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ValidateActivation(activationId, bindings);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var bindingSession = unitOfWork.OpenSession(bindingUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var existingState = ReadProjectionState(stateSession, activationId);
        var existingRows = ListAllByActivation(bindingSession, activationId, cancellationToken);
        var prepared = bindings.Select(binding => binding with { IsActive = false }).ToArray();

        if (existingState is not null)
        {
            if (!existingState.Value.State.IsActive &&
                ProjectionMatches(existingState.Value.State, existingRows) &&
                ProjectionsEqual(existingRows, prepared))
                return;

            throw new InvalidOperationException(
                $"Trigger-binding activation projection '{activationId}' is already prepared with different state.");
        }

        var existingById = existingRows.ToDictionary(row => row.Binding.TriggerBindingId, StringComparer.Ordinal);
        var desiredById = prepared.ToDictionary(binding => binding.TriggerBindingId, StringComparer.Ordinal);
        foreach (var existing in existingRows)
        {
            if (desiredById.ContainsKey(existing.Binding.TriggerBindingId))
                StageUpsert(unitOfWork, desiredById[existing.Binding.TriggerBindingId], existing.Version);
            else
                StageDelete(unitOfWork, bindingUnit, existing.Binding.TriggerBindingId, existing.Version);
        }

        foreach (var binding in prepared.Where(binding => !existingById.ContainsKey(binding.TriggerBindingId)))
            StageInsert(unitOfWork, binding);
        var projectionState = ProjectionState(activationId, prepared, isActive: false);
        StageProjectionState(
            unitOfWork,
            projectionState,
            expectedVersion: null,
            createOnly: true);
        try
        {
            await CommitAsync(unitOfWork, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A concurrent prepare may have committed the exact same inactive projection before this
            // acknowledgement arrived. Reconcile only that exact state; never treat a different winner
            // or a partially visible projection as an idempotent success.
            try
            {
                var stateAfter = ReadProjectionState(OpenProjectionStateSession(), activationId);
                var rowsAfter = ListAllByActivation(OpenBindingSession(), activationId, cancellationToken);
                if (stateAfter is { State.IsActive: false } &&
                    ProjectionMatches(stateAfter.Value.State, rowsAfter) &&
                    ProjectionsEqual(rowsAfter, prepared))
                    return;
            }
            catch
            {
                // Preserve the original provider failure when reconciliation cannot establish convergence.
            }

            throw;
        }
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(
        WorkflowTriggerBindingActivationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(QueryPage(
            query,
            Equal(ElsaRuntimeV2StorageManifest.ActivationIdField, query.ActivationId),
            cancellationToken));
    }

    public async ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (replacedActivationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedActivationId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var bindingSession = unitOfWork.OpenSession(bindingUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var candidate = ReadProjectionState(stateSession, activationId)
            ?? throw new InvalidOperationException(
                $"Activation '{activationId}' has no prepared trigger-binding projection.");
        var hasDistinctReplacement = replacedActivationId is not null &&
            !StringComparer.Ordinal.Equals(activationId, replacedActivationId);
        var replaced = hasDistinctReplacement
            ? ReadProjectionState(stateSession, replacedActivationId!)
            : null;
        var candidateRows = ListAllByActivation(bindingSession, activationId, cancellationToken);
        EnsureProjectionMatches(candidate.State, candidateRows);

        if (candidate.State.IsActive)
        {
            if (!hasDistinctReplacement || replaced is null || !replaced.Value.State.IsActive)
            {
                if (candidateRows.Any(row => !row.Binding.IsActive))
                {
                    throw new InvalidDataException(
                        $"Trigger-binding activation '{activationId}' is active but has non-active rows.");
                }

                return;
            }
            throw new InvalidOperationException(
                $"Trigger-binding activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
        }

        if (hasDistinctReplacement && (replaced is null || !replaced.Value.State.IsActive))
        {
            throw new InvalidOperationException(
                $"Trigger-binding activation '{activationId}' cannot replace a projection that is missing or no longer active.");
        }

        var replacedRows = hasDistinctReplacement
            ? ListAllByActivation(bindingSession, replacedActivationId!, cancellationToken)
            : [];
        if (replaced is not null)
            EnsureProjectionMatches(replaced.Value.State, replacedRows);
        foreach (var row in candidateRows)
            StageUpsert(unitOfWork, row.Binding with { IsActive = true }, row.Version);
        foreach (var row in replacedRows)
            StageUpsert(unitOfWork, row.Binding with { IsActive = false }, row.Version);
        StageProjectionState(
            unitOfWork,
            candidate.State with { IsActive = true },
            candidate.Version,
            createOnly: false);
        if (replaced is not null)
        {
            StageProjectionState(
                unitOfWork,
                replaced.Value.State with { IsActive = false },
                replaced.Value.Version,
                createOnly: false);
        }

        await CommitAsync(unitOfWork, cancellationToken);
    }

    public async ValueTask DeleteByActivationAsync(
        string activationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var bindingSession = unitOfWork.OpenSession(bindingUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var rows = ListAllByActivation(bindingSession, activationId, cancellationToken);
        var state = ReadProjectionState(stateSession, activationId);
        foreach (var row in rows)
            StageDelete(unitOfWork, bindingUnit, row.Binding.TriggerBindingId, row.Version);
        if (state is not null)
        {
            unitOfWork.Stage(RowWrite.Delete(
                projectionStateUnit,
                GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)),
                WriteOptions.IfVersion(state.Value.Version)));
        }

        await CommitAsync(unitOfWork, cancellationToken);
    }

    public async ValueTask<int> DeleteByArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        RequireAtomicCommit();

        using var unitOfWork = BeginUnitOfWork();
        var bindingSession = unitOfWork.OpenSession(bindingUnit);
        var stateSession = unitOfWork.OpenSession(projectionStateUnit);
        var rows = ListAll(bindingSession, Equal(ElsaRuntimeV2StorageManifest.ArtifactIdField, artifactId), cancellationToken);
        var activationRows = rows
            .Where(row => row.Binding.ActivationId is not null)
            .GroupBy(row => row.Binding.ActivationId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ListAllByActivation(bindingSession, group.Key, cancellationToken),
                StringComparer.Ordinal);
        var statesToDelete = new List<(string ActivationId, long Version)>();
        foreach (var (activationId, allRows) in activationRows)
        {
            if (allRows.Any(row => !StringComparer.Ordinal.Equals(row.Binding.ArtifactId, artifactId)))
            {
                throw new InvalidOperationException(
                    $"Cannot delete artifact '{artifactId}' because activation '{activationId}' also contains another artifact.");
            }

            var state = ReadProjectionState(stateSession, activationId);
            if (state is not null)
            {
                EnsureProjectionMatches(state.Value.State, allRows);
                statesToDelete.Add((activationId, state.Value.Version));
            }
        }

        foreach (var row in rows)
            StageDelete(unitOfWork, bindingUnit, row.Binding.TriggerBindingId, row.Version);
        foreach (var (activationId, version) in statesToDelete)
        {
            unitOfWork.Stage(RowWrite.Delete(
                projectionStateUnit,
                GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)),
                WriteOptions.IfVersion(version)));
        }
        await CommitAsync(unitOfWork, cancellationToken);
        return rows.Count;
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
        WorkflowTriggerBindingPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(QueryPage(
            query,
            And(
                Equal(
                    ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
                    GroundworkV2BookmarkStorageConventions.StimulusLookupKey(query.StimulusType, query.StimulusHash)),
                Equal(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField, true)),
            cancellationToken));
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
        WorkflowTriggerBindingArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(QueryPage(
            query,
            Equal(ElsaRuntimeV2StorageManifest.ArtifactIdField, query.ArtifactId),
            cancellationToken));
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
        WorkflowTriggerBindingTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(QueryPage(
            query,
            And(
                Equal(
                    ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
                    GroundworkV2BookmarkStorageConventions.StimulusTypeLookupKey(query.StimulusType)),
                Equal(ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField, true)),
            cancellationToken));
    }

    private WorkflowTriggerBindingPage QueryPage(
        WorkflowTriggerBindingPageRequest query,
        Predicate predicate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = OpenBindingSession().Query(new QueryRequest(
            new TableId(bindingUnit.Name),
            predicate,
            [new OrderTerm(Column(ElsaRuntimeV2StorageManifest.TriggerBindingIdField), OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken),
            ResultShape.TotalCount.Instance));
        var totalCount = result.TotalCount ?? throw new InvalidDataException(
            "Groundwork trigger-binding query did not return its requested filtered total count.");
        return new WorkflowTriggerBindingPage(
            query,
            result.Rows.Select(GroundworkV2WorkflowTriggerBindingStorageConventions.Deserialize).ToArray(),
            totalCount,
            result.NextContinuationToken);
    }

    private List<StoredBinding> ListAllByActivation(
        IStorageSession session,
        string activationId,
        CancellationToken cancellationToken) =>
        ListAll(session, Equal(ElsaRuntimeV2StorageManifest.ActivationIdField, activationId), cancellationToken);

    private List<StoredBinding> ListAll(
        IStorageSession session,
        Predicate predicate,
        CancellationToken cancellationToken)
    {
        var rows = new List<StoredBinding>();
        string? continuation = null;
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                new TableId(bindingUnit.Name),
                predicate,
                [new OrderTerm(Column(ElsaRuntimeV2StorageManifest.TriggerBindingIdField), OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                PagingFor(WorkflowTriggerBindingPageRequest.MaximumLimit, continuation)));
            if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
                throw new InvalidOperationException("Groundwork trigger-binding query returned a continuation after an empty page.");

            foreach (var values in result.Rows)
            {
                var binding = GroundworkV2WorkflowTriggerBindingStorageConventions.Deserialize(values);
                var id = binding.TriggerBindingId;
                var entry = session.Read(GroundworkRuntimeRowStore.Key(id));
                if (entry is null)
                    continue;
                var current = GroundworkV2WorkflowTriggerBindingStorageConventions.Deserialize(entry.Values.Values);
                if (!StringComparer.Ordinal.Equals(current.TriggerBindingId, id))
                    throw new InvalidDataException("Groundwork trigger-binding query row identity changed while being materialized.");
                rows.Add(new StoredBinding(current, entry.Version ?? throw new InvalidDataException(
                    $"Groundwork trigger-binding row '{id}' did not expose an optimistic revision.")));
            }

            continuation = result.NextContinuationToken;
            if (continuation is not null && !seenContinuations.Add(continuation))
                throw new InvalidOperationException("Groundwork trigger-binding query repeated a continuation token.");
        } while (continuation is not null);

        return rows;
    }

    private static bool ProjectionsEqual(
        IReadOnlyCollection<StoredBinding> existing,
        IReadOnlyCollection<WorkflowTriggerBinding> prepared)
    {
        if (existing.Count != prepared.Count)
            return false;

        var actualById = existing.ToDictionary(row => row.Binding.TriggerBindingId, StringComparer.Ordinal);
        foreach (var expected in prepared)
        {
            if (!actualById.TryGetValue(expected.TriggerBindingId, out var actual) ||
                actual.Binding.IsActive ||
                !StringComparer.Ordinal.Equals(
                    GroundworkV2RuntimeJson.Serialize(actual.Binding with { IsActive = false }),
                    GroundworkV2RuntimeJson.Serialize(expected)))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureProjectionMatches(
        GroundworkV2WorkflowTriggerBindingProjectionState state,
        IReadOnlyCollection<StoredBinding> rows)
    {
        if (!ProjectionMatches(state, rows))
        {
            throw new InvalidDataException(
                $"Groundwork trigger-binding activation projection '{state.ActivationId}' does not match its projection state.");
        }
    }

    private static bool ProjectionMatches(
        GroundworkV2WorkflowTriggerBindingProjectionState state,
        IReadOnlyCollection<StoredBinding> rows) =>
        state.ProjectionKind == ProjectionKind &&
        rows.All(row => StringComparer.Ordinal.Equals(row.Binding.ActivationId, state.ActivationId)) &&
        state.BindingCount == rows.Count &&
        StringComparer.Ordinal.Equals(
            state.ProjectionFingerprint,
            ProjectionFingerprint(rows.Select(row => row.Binding)));

    private static GroundworkV2WorkflowTriggerBindingProjectionState ProjectionState(
        string activationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        bool isActive) =>
        new(
            ProjectionKind,
            activationId,
            isActive,
            bindings.Count,
            ProjectionFingerprint(bindings));

    private static string ProjectionFingerprint(IEnumerable<WorkflowTriggerBinding> bindings)
    {
        var canonical = GroundworkV2RuntimeJson.Serialize(
            bindings
                .Select(binding => binding with { IsActive = false })
                .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private void StageUpsert(
        IUnitOfWork unitOfWork,
        WorkflowTriggerBinding binding,
        long expectedVersion)
    {
        unitOfWork.Stage(RowWrite.Upsert(
            bindingUnit,
            GroundworkV2WorkflowTriggerBindingStorageConventions.Values(binding),
            WriteOptions.IfVersion(expectedVersion)));
    }

    private void StageInsert(IUnitOfWork unitOfWork, WorkflowTriggerBinding binding) =>
        unitOfWork.Stage(RowWrite.Insert(
            bindingUnit,
            GroundworkV2WorkflowTriggerBindingStorageConventions.Values(binding),
            WriteOptions.CreateOnly));

    private static void StageDelete(
        IUnitOfWork unitOfWork,
        StorageUnit unit,
        string id,
        long version) =>
        unitOfWork.Stage(RowWrite.Delete(unit, GroundworkRuntimeRowStore.Key(id), WriteOptions.IfVersion(version)));

    private void StageProjectionState(
        IUnitOfWork unitOfWork,
        GroundworkV2WorkflowTriggerBindingProjectionState state,
        long? expectedVersion,
        bool createOnly)
    {
        var options = createOnly
            ? WriteOptions.CreateOnly
            : WriteOptions.IfVersion(expectedVersion ?? throw new InvalidOperationException(
                $"Projection state '{state.ActivationId}' did not expose an optimistic revision."));
        unitOfWork.Stage(RowWrite.Upsert(
            projectionStateUnit,
            GroundworkRuntimeRowStore.Values(
                ProjectionStateId(state.ActivationId),
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                GroundworkV2RuntimeJson.Serialize(state)),
            options));
    }

    private (GroundworkV2WorkflowTriggerBindingProjectionState State, long Version)? ReadProjectionState(
        IStorageSession session,
        string activationId)
    {
        var entry = session.Read(GroundworkRuntimeRowStore.Key(ProjectionStateId(activationId)));
        if (entry is null)
            return null;
        var state = DeserializeProjectionState(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(state.ActivationId, activationId))
            throw new InvalidDataException("Groundwork activation projection state identity does not match its key.");
        return (state, entry.Version ?? throw new InvalidDataException(
            $"Groundwork activation projection state '{activationId}' did not expose an optimistic revision."));
    }

    private static GroundworkV2WorkflowTriggerBindingProjectionState DeserializeProjectionState(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException("Groundwork activation projection state returned an unsupported schema version.");
        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                System.Text.Json.JsonElement element => element.GetRawText(),
                System.Text.Json.JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork activation projection state content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork activation projection state did not contain JSON content.");
        var state = GroundworkV2RuntimeJson.Deserialize<GroundworkV2WorkflowTriggerBindingProjectionState>(content)
            ?? throw new InvalidDataException("Groundwork activation projection state content was empty.");
        if (!StringComparer.Ordinal.Equals(state.ProjectionKind, ProjectionKind) ||
            string.IsNullOrWhiteSpace(state.ActivationId) ||
            state.BindingCount < 0 ||
            string.IsNullOrWhiteSpace(state.ProjectionFingerprint))
            throw new InvalidDataException("Groundwork activation projection state has an invalid trigger-binding identity.");
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, ProjectionStateId(state.ActivationId));
        return state;
    }

    private IStorageSession OpenBindingSession() => sessions.Open(
        bindingUnit.Id.Value,
        Access,
        targetName);

    private IStorageSession OpenProjectionStateSession() => sessions.Open(
        projectionStateUnit.Id.Value,
        Access,
        targetName);

    private IUnitOfWork BeginUnitOfWork() => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        [
            ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingDocumentKind,
            ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind
        ],
        targetName);

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current ??
                          throw new InvalidOperationException("Workflow trigger-binding persistence access context is missing.");
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork workflow trigger bindings require one explicit persistence scope; global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork workflow trigger-binding activation changes require the provider's evidenced atomic-commit capability.");
        }
    }

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        WorkflowTriggerBinding binding)
    {
        _ = GroundworkV2WorkflowTriggerBindingStorageConventions.Deserialize(existing.Values.Values);
        var revision = existing.Version ?? throw new InvalidDataException(
            $"Groundwork workflow trigger-binding row '{binding.TriggerBindingId}' did not expose an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic workflow trigger-binding concurrency.");
        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private ColumnRef Column(string name)
    {
        var definition = bindingUnit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork trigger-binding unit '{bindingUnit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork trigger-binding query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(new TableId(bindingUnit.Name), name, type, definition.IsNullable, definition.MaxLength);
    }

    private Predicate Equal(string field, object value) =>
        new Predicate.Equal(Column(field), QueryConstant.Of(Column(field), value));

    private static Predicate And(params Predicate[] predicates) => new Predicate.And(predicates);

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static string ProjectionStateId(string activationId) =>
        $"{ProjectionKind}:{activationId.Length}:{activationId}";

    private static void ValidateActivation(
        string activationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentNullException.ThrowIfNull(bindings);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            GroundworkV2WorkflowTriggerBindingStorageConventions.Validate(binding);
            if (!StringComparer.Ordinal.Equals(binding.ActivationId, activationId))
                throw new ArgumentException(
                    $"Binding '{binding.TriggerBindingId}' does not belong to activation '{activationId}'.",
                    nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
            if (!ids.Add(binding.TriggerBindingId))
                throw new ArgumentException(
                    $"Activation '{activationId}' contains duplicate trigger binding '{binding.TriggerBindingId}'.",
                    nameof(bindings));
        }
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        if (!values.TryGetValue(field, out var actual) ||
            (actual is string text ? !StringComparer.Ordinal.Equals(text, expected) :
             actual is not System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element ||
             !StringComparer.Ordinal.Equals(element.GetString(), expected)))
        {
            throw new InvalidDataException(
                $"Groundwork activation projection state projection '{field}' does not match its current content.");
        }
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) switch
        {
            true when value is string text && !string.IsNullOrWhiteSpace(text) => text,
            true when value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element &&
                       !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            _ => throw new InvalidDataException(
                $"Groundwork activation projection state is missing required string field '{field}'.")
        };

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static async ValueTask CommitAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        BatchWriteReport report;
        try
        {
            report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's original exception.
            }

            throw;
        }

        if (!report.IsSuccessful)
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's attributed failure.
            }

            throw new InvalidOperationException(
                $"Groundwork rejected workflow trigger-binding activation change with {report.Failed} failed row outcomes.");
        }
    }

    private sealed record StoredBinding(WorkflowTriggerBinding Binding, long Version);
}
