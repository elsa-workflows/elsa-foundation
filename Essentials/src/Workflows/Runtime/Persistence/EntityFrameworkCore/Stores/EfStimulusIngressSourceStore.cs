using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Canonical source installation. Claim/seal delivery and producer registration are separate integration work.</summary>
internal sealed class EfStimulusIngressSourceStore(RuntimeDbContext context,
    IPersistenceAccessContextAccessor access, IWorkflowExecutionPartitionAccessor partitions,
    IWorkflowExecutableStore material, IWorkflowExecutableSourceReferenceStore references,
    IWorkflowExecutionStateStore executions, IWorkflowExecutableRootWriteLeaseManager rootLeases,
    TimeProvider clock, EfWorkflowTestScopeStore? testScopes = null) : IStimulusIngressSourceStore
{
    public async ValueTask<StimulusIngressSource?> FindByKeyAsync(StimulusDeliveryScope origin, string clientKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        var scope = RequireOrigin(origin);
        EnsureCommittedRead();
        var id = KeyedRowId(origin, clientKey);
        var row = await context.StimulusIngressSources.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id,
            cancellationToken);
        if (row is null) return null;
        var source = ReadChecked(row, scope);
        if (!source.MatchesOrigin(origin) || source.ClientKey != clientKey)
            throw new InvalidDataException("The canonical ingress key has contradictory origin or sender identity.");
        return source;
    }

    public async ValueTask<StimulusIngressSource?> FindByIdAsync(StimulusDeliveryScope origin, string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var scope = RequireOrigin(origin);
        EnsureCommittedRead();
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var idHash = EfRelationalIdentity.Hash(sourceId);
        var row = await context.StimulusIngressSources.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ScopeKeyHash == scopeHash && candidate.SourceIdHash == idHash, cancellationToken);
        if (row is null) return null;
        var source = ReadChecked(row, scope);
        if (source.SourceId != sourceId) throw new InvalidDataException("The canonical ingress identity projection conflicts.");
        return source.MatchesOrigin(origin) ? source : null;
    }

    public async ValueTask<StimulusIngressSource> InstallAsync(StimulusIngressSource candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireOrigin(candidate.OriginScope);
        EnsureCommittedRead();
        RequireBoundary(scope);
        RequireAdmittedVersion(RuntimeStimulusIngressEfModule.SchemaFamily, RuntimeStimulusIngressEfModule.SchemaVersion);
        // A new family is invisible to an old material maintainer. The source protocol requires the
        // existing material family's new retention reader before it can create any roots.
        RequireAdmittedVersion(RuntimeArtifactEfModule.SchemaFamily, "2.0.0");
        RequireAdmittedVersion(RuntimeWorkflowExecutionEfModule.SchemaFamily, RuntimeWorkflowExecutionEfModule.IngressRetentionSchemaVersion);
        var existing = await ReadWinnerAsync(candidate, scope, cancellationToken);
        if (existing is not null) return SameRequest(candidate, existing);
        if ((candidate.OriginScope.RunKind == WorkflowRunKind.TestRun) != (candidate.OriginScope.TestScope is not null))
            throw new ArgumentException("A new ingress test origin requires its explicit test scope and run kind together.", nameof(candidate));
        if (candidate.AcceptedAt > clock.GetUtcNow())
            throw new InvalidDataException("The ingress admission time lies after the provider clock.");
        var roots = candidate.GetPinnedExecutables();
        var resolver = new WorkflowExecutableStartSourceResolver(references, clock);
        var physicalMaterial = (EfWorkflowExecutableStore)material.GetMaterialStore();
        StimulusIngressSource? accepted = null;

        try
        {
            if (roots.Count == 0)
                await CommitAsync([], cancellationToken);
            else
                await rootLeases.ExecuteFencedAsync(roots, "stimulus-ingress:" + candidate.SourceId + ":" + Guid.NewGuid().ToString("N"),
                    CommitAsync, cancellationToken);
            return accepted ?? throw new InvalidOperationException("Ingress acceptance returned without a committed winner.");
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            // A unique race or lost acknowledgement may have committed the winner. Read that actual
            // key before any retry/effect. If the read fails or proves no winner, preserve the failure.
            context.ChangeTracker.Clear();
            var winner = await ReadWinnerAsync(candidate, scope, cancellationToken);
            if (winner is not null) return SameRequest(candidate, winner);
            throw;
        }

        async ValueTask CommitAsync(IReadOnlyList<WorkflowExecutableRootWriteLease> grants, CancellationToken token)
        {
            IReadOnlyCollection<RuntimeExecutionMaterial> materialSnapshot = roots.Count == 0 ? [] : await physicalMaterial.ReadMaintenanceMaterialAsync(scope, token);
            var closure = WorkflowExecutableDependencyGraph.ResolveMaterialClosure(roots, materialSnapshot);
            var expectedIds = closure.Select(item => item.Identity.ArtifactId).Order(StringComparer.Ordinal).ToArray();
            if (grants.Count != expectedIds.Length || grants.Any(grant => grant is null) ||
                !grants.Select(grant => grant.ArtifactId).Order(StringComparer.Ordinal).SequenceEqual(expectedIds))
                throw new InvalidDataException("Ingress acceptance requires the exact complete material-closure grant set.");
            foreach (var root in roots)
                if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(
                    closure.Single(item => item.Identity.ArtifactId == root.ArtifactId).Identity, root))
                    throw new InvalidDataException("The ingress executable pin changed before acceptance.");
            // Ordinary authoritative start attribution is validated under the same complete root set.
            await resolver.ValidateCapturedInScopeAsync(candidate.Cohort.Starts, candidate.Scope, token);
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            try
            {
                IReadOnlyList<(WorkflowExecutableRootWriteLease Grant, DateTimeOffset ExpiresAt)> deadlines = grants.Count == 0 ? [] :
                    await physicalMaterial.FenceRootWriteLeasesAsync(grants, clock, token);
                var winner = await ReadWinnerAsync(candidate, scope, token);
                if (winner is not null) { accepted = SameRequest(candidate, winner); return; }
                if (candidate.OriginScope.TestScope is { } testScope)
                {
                    if (testScopes is null || !testScopes.Uses(context, candidate.Scope))
                        throw new NotSupportedException("Test ingress requires the same physical test-scope admission store.");
                    await testScopes.AssertOpenAsync(testScope, clock.GetUtcNow(), token);
                }
                else if (candidate.OriginScope.RunKind != WorkflowRunKind.PublishedRun)
                    throw new NotSupportedException("This ingress origin requires an explicit runtime admission owner.");
                // This upgrades only mutable coordination/source-reference envelopes, under their
                // normal grants; executable content-addressed bytes are not rewritten.
                await physicalMaterial.ProtectIngressRetentionAsync(grants, candidate.Cohort.Starts, token);
                EnsureLive();
                await context.SaveChangesAsync(token);
                EnsureLive();
                // The reference revision locks now survive through the source commit. A deletion
                // before the lock was acquired loses the CAS or the exact-source validation here.
                await resolver.ValidateCapturedInScopeAsync(candidate.Cohort.Starts, candidate.Scope, token);
                foreach (var dependency in candidate.GetExecutionDependencies())
                    await StimulusSourceExecutionFences.ProtectIngressAsync(context, scope, dependency, token);
                context.StimulusIngressSources.Add(ToEntity(candidate, scope));
                EnsureLive();
                await context.SaveChangesAsync(token);
                EnsureLive();
                await transaction.CommitAsync(token);
                accepted = candidate;

                void EnsureLive()
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var deadline in deadlines)
                        if (deadline.ExpiresAt <= clock.GetUtcNow())
                            throw new WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
                }
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                throw;
            }
            finally { context.ChangeTracker.Clear(); }
        }
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(IWorkflowExecutableStore selectedMaterial,
        IWorkflowExecutableSourceReferenceStore selectedReferences, IWorkflowExecutionStateStore selectedExecutions,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(selectedMaterial.GetMaterialStore(), material.GetMaterialStore()) ||
            !ReferenceEquals(selectedReferences, references) || !ReferenceEquals(selectedExecutions, executions))
            throw new NotSupportedException("Ingress retention must use its actual selected material/reference/execution stores.");
        var scope = access.Current.RequireScope(admitPrivileged: true).Value;
        RequireBoundary(scope);
        EnsureCommittedRead();
        return (await ReadRetainedSourcesAsync(context, scope, cancellationToken))
            .SelectMany(source => source.GetPinnedExecutables()).Select(pin => pin.ArtifactId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static async ValueTask<IReadOnlyList<StimulusIngressSource>> ReadRetainedSourcesAsync(RuntimeDbContext context,
        string scope, CancellationToken cancellationToken)
    {
        var encoded = EfRelationalIdentity.Encode(scope);
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var sources = new List<StimulusIngressSource>();
        string? after = null;
        while (true)
        {
            // Include either projection so a contradictory current-scope envelope cannot disappear
            // behind the narrower index. Every returned row is checked against its canonical body.
            var query = context.StimulusIngressSources.AsNoTracking()
                .Where(row => row.ScopeKey == encoded || row.ScopeKeyHash == scopeHash);
            if (after is not null) query = query.Where(row => row.Id.CompareTo(after) > 0);
            var rows = await query.OrderBy(row => row.Id).Take(RuntimeStorePageRequest.MaximumLimit).ToArrayAsync(cancellationToken);
            foreach (var row in rows) sources.Add(ReadChecked(row, scope));
            if (rows.Length < RuntimeStorePageRequest.MaximumLimit) return sources.AsReadOnly();
            after = rows[^1].Id;
        }
    }

    internal static async ValueTask<bool> IsMaterialRetainedAsync(RuntimeDbContext context,
        IPersistenceAccessContextAccessor access, string scope, string artifactId, CancellationToken cancellationToken)
    {
        var roots = (await ReadRetainedSourcesAsync(context, scope, cancellationToken))
            .SelectMany(source => source.GetPinnedExecutables()).Distinct().ToArray();
        if (roots.Length == 0) return false;
        var materials = await new EfWorkflowExecutableStore(context, access).ReadMaintenanceMaterialAsync(scope, cancellationToken);
        return WorkflowExecutableDependencyGraph.ResolveMaterialClosure(roots, materials)
            .Any(material => material.Identity.ArtifactId == artifactId);
    }

    internal static StimulusIngressSource ReadChecked(StimulusIngressSourceEntity row, string scope)
    {
        if (EfSchemaVersion.NotReadable(RuntimeStimulusIngressEfModule.Chain, row.SchemaVersion))
            throw new NotSupportedException("The canonical ingress source format is unreadable.");
        var source = StimulusIngressJson.Read(row.ContentJson);
        if (source.Scope.Value != scope || row.ScopeKey != EfRelationalIdentity.Encode(scope) ||
            row.ScopeKeyHash != EfRelationalIdentity.Hash(scope) || row.Id != RowId(source) ||
            row.SourceId != EfRelationalIdentity.Encode(source.SourceId) || row.SourceIdHash != EfRelationalIdentity.Hash(source.SourceId) ||
            row.OriginFingerprint != RuntimeCheckpointCommitFingerprint.ComputeIngressOrigin(source.OriginScope) ||
            row.SourceFingerprint != RuntimeCheckpointCommitFingerprint.ComputeIngressSource(source) ||
            row.AcceptedAtUtcTicks != source.AcceptedAt.UtcTicks || row.Revision <= 0)
            throw new InvalidDataException("The persisted ingress source envelope contradicts its canonical content.");
        return source;
    }

    private async ValueTask<StimulusIngressSource?> ReadWinnerAsync(StimulusIngressSource candidate, string scope, CancellationToken token)
    {
        var id = RowId(candidate);
        var row = await context.StimulusIngressSources.AsNoTracking().SingleOrDefaultAsync(source => source.Id == id, token);
        return row is null ? null : ReadChecked(row, scope);
    }

    private static StimulusIngressSource SameRequest(StimulusIngressSource candidate, StimulusIngressSource winner) =>
        candidate.MatchesOrigin(winner.OriginScope) && candidate.ClientKey == winner.ClientKey &&
        candidate.RequestFingerprint == winner.RequestFingerprint &&
        (candidate.ClientKey is not null || candidate.SourceId == winner.SourceId)
            ? winner : throw new InvalidOperationException("The ingress key already identifies a different semantic request.");

    private string RequireOrigin(StimulusDeliveryScope origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var scope = access.Current.RequireScope();
        access.Current.EnsureScope(origin.PersistenceScope);
        access.Current.EnsureTenantScope(origin.TenantId);
        if (origin.Partition != partitions.Current)
            throw new InvalidOperationException("Ingress origin does not match the selected runtime partition.");
        return scope.Value;
    }

    private void RequireBoundary(string scope)
    {
        if (material.GetMaterialStore() is not EfWorkflowExecutableStore artifacts ||
            references is not EfWorkflowExecutableSourceReferenceStore sourceReferences || executions is not EfWorkflowExecutionStateStore states ||
            !ReferenceEquals(artifacts.Context, context) || !ReferenceEquals(sourceReferences.Context, context) || !ReferenceEquals(states.Context, context) ||
            artifacts.AccessContext.RequireScope(admitPrivileged: true).Value != scope ||
            sourceReferences.AccessContext.RequireScope(admitPrivileged: true).Value != scope || states.AccessContext.RequireScope(admitPrivileged: true).Value != scope)
            throw new NotSupportedException("Ingress requires one coherent physical material/reference/execution context and scope.");
    }

    private void EnsureCommittedRead()
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || Transaction.Current is not null)
            throw new InvalidOperationException("Canonical ingress access requires a clean context and a committed boundary.");
    }

    private void RequireAdmittedVersion(string family, string required)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(family) ?? throw new NotSupportedException("Ingress requires its actual admitted schema participants.");
        if (state.WritesRefused || state.WriteVersion != required)
            throw new NotSupportedException("Ingress requires finalized source and material-retention formats before admission.");
    }

    private static StimulusIngressSourceEntity ToEntity(StimulusIngressSource source, string scope) => new()
    {
        Id = RowId(source), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        SourceId = EfRelationalIdentity.Encode(source.SourceId), SourceIdHash = EfRelationalIdentity.Hash(source.SourceId),
        OriginFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIngressOrigin(source.OriginScope),
        SourceFingerprint = RuntimeCheckpointCommitFingerprint.ComputeIngressSource(source), AcceptedAtUtcTicks = source.AcceptedAt.UtcTicks,
        ContentJson = StimulusIngressJson.Write(source), SchemaVersion = RuntimeStimulusIngressEfModule.SchemaVersion, Revision = 1
    };

    private static string RowId(StimulusIngressSource source) => source.ClientKey is { } key
        ? KeyedRowId(source.OriginScope, key)
        : EfRelationalIdentity.HashLengthFramed([source.Scope.Value, "stimulus-ingress.source", source.SourceId]);

    private static string KeyedRowId(StimulusDeliveryScope origin, string key) =>
        EfRelationalIdentity.HashLengthFramed([origin.PersistenceScope.Value, "stimulus-ingress.key",
            RuntimeCheckpointCommitFingerprint.ComputeIngressKey(origin, key)]);
}
