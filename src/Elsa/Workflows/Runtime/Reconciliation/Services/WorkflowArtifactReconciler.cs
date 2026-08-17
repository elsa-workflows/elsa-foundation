using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Reconciliation.Services;

/// <summary>
/// Default <see cref="IWorkflowArtifactReconciler"/>: imports and activates the closures every registered
/// <see cref="IWorkflowArtifactReconciliationSource"/> offers.
/// </summary>
/// <remarks>
/// <para>
/// The gates run in a fixed order and every one of them completes for the <b>whole closure unit</b> before the
/// first write. That ordering is the design, not an implementation detail: a unit that fails any gate must leave
/// the engine exactly as it found it, and a gate that ran after a sibling had already been persisted could not
/// promise that.
/// </para>
/// <para>
/// <b>Activation is one call.</b> The importer never takes a lease, writes a projection, notifies an observer or
/// compensates — <see cref="IWorkflowActivationCoordinator"/> owns that entire sequence for every path, and a
/// second copy of it here would be exactly the duplicated authority FR-B-006 exists to remove. The importer's
/// recovery unit is the next reconcile pass.
/// </para>
/// </remarks>
public sealed class WorkflowArtifactReconciler(
    IEnumerable<IWorkflowArtifactReconciliationSource> sources,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableHasher hasher,
    IWorkflowTriggerBindingExtractor triggerBindingExtractor,
    IWorkflowActivationAuthority activationAuthority,
    IWorkflowActivationCoordinator activationCoordinator,
    TimeProvider timeProvider,
    ILogger<WorkflowArtifactReconciler> logger) : IWorkflowArtifactReconciler
{
    /// <summary>
    /// The activation lane imported artifacts claim. Matches publishing's default so that a definition arriving
    /// through both paths contends for the <em>same</em> slot and the ownership rule can actually fire — separate
    /// lanes would let both sides "succeed" and double-activate.
    /// </summary>
    public const string DefaultSlotName = "default";

    private const string ActivationIdPrefix = "import";

    private readonly IReadOnlyCollection<IWorkflowArtifactReconciliationSource> _sources = sources.ToArray();

    public async ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (_sources.Count == 0)
        {
            logger.LogDebug("No workflow artifact reconciliation sources are composed; nothing to reconcile.");
            return WorkflowArtifactReconciliationResult.Empty;
        }

        var entries = new List<WorkflowArtifactImportEntry>();

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReconcileSourceAsync(source, entries, cancellationToken);
        }

        logger.LogInformation(
            "Workflow artifact reconciliation completed: {Imported} imported, {AlreadyCurrent} already current, {Skipped} skipped, {Rejected} rejected.",
            entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Imported),
            entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.AlreadyCurrent),
            entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Skipped),
            entries.Count(entry => entry.Outcome == WorkflowArtifactImportOutcome.Rejected));

        return new WorkflowArtifactReconciliationResult(entries);
    }

    /// <summary>
    /// Runs every closure unit one source offers.
    /// </summary>
    /// <remarks>
    /// A file that cannot be decoded at all surfaces as <see cref="InvalidWorkflowArtifactClosureException"/> from
    /// inside the source's iterator. It is recorded as a rejection and this source's enumeration ends — a C#
    /// async iterator cannot be resumed after its body throws, so "skip the bad file and read the next one" is not
    /// available behind the pinned <c>IAsyncEnumerable</c> shape. Other sources still run. A pass-aborting
    /// <see cref="WorkflowArtifactReconciliationException"/> deliberately propagates: a mount that is not there is
    /// not the same as a mount that is empty.
    /// </remarks>
    private async ValueTask ReconcileSourceAsync(
        IWorkflowArtifactReconciliationSource source,
        List<WorkflowArtifactImportEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var file in source.ReadAsync(cancellationToken).WithCancellation(cancellationToken))
                await ReconcileUnitAsync(source, file, entries, cancellationToken);
        }
        catch (InvalidWorkflowArtifactClosureException exception)
        {
            logger.LogError(
                exception,
                "Workflow artifact closure at '{Origin}' could not be read; source '{SourceId}' stopped after it.",
                exception.Origin,
                source.SourceId);
            entries.Add(WorkflowArtifactImportEntry.Rejected(
                exception.Origin,
                source.SourceId,
                string.Empty,
                null,
                null,
                WorkflowArtifactRejectionKind.MalformedClosure,
                exception.Message));
        }
    }

    private async ValueTask ReconcileUnitAsync(
        IWorkflowArtifactReconciliationSource source,
        WorkflowArtifactClosureFile file,
        List<WorkflowArtifactImportEntry> entries,
        CancellationToken cancellationToken)
    {
        var closure = file.Closure;

        // Gate 1+2 — the envelope validates against itself alone: every dependency edge resolves inside the
        // carried set, declared hashes agree, no duplicate identities, no cycles.
        var plan = WorkflowArtifactClosurePlanner.Plan(closure);
        if (!plan.IsValid)
        {
            RejectUnit(entries, source, file, plan.Members, plan.RejectionKind, plan.Diagnostic!);
            return;
        }

        // Gate 2a — recompute every member's canonical content hash BEFORE anything persists. The executable
        // store is create-only and dedups by a content-addressed id, so persisting an unverified payload under a
        // claimed id would let a corrupted file *become* that id's content on a fresh engine, permanently.
        foreach (var member in plan.Members)
        {
            if (TryFindContentHashFault(member) is { } fault)
            {
                RejectUnit(entries, source, file, plan.Members, WorkflowArtifactRejectionKind.ContentHashMismatch, fault);
                return;
            }
        }

        // Gate 2b — the carried trigger surface is an expectation to check, never rows to import. A disagreement
        // between what the exporter said the artifact's triggers are and what this runtime extracts from the very
        // same payload means the two sides do not agree on what the artifact does.
        foreach (var member in plan.Members)
        {
            if (TryFindTriggerSurfaceFault(member, closure) is { } fault)
            {
                RejectUnit(entries, source, file, plan.Members, WorkflowArtifactRejectionKind.TriggerSurfaceMismatch, fault);
                return;
            }
        }

        // Persist every member first. Order-free by construction: the store is content-addressed and create-only,
        // so a save is either a no-op or a first write, and no member's persistence depends on another's.
        foreach (var member in plan.Members)
        {
            try
            {
                await executableStore.SaveAsync(member, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Workflow artifact '{ArtifactId}' from '{Origin}' could not be persisted.",
                    member.Identity.ArtifactId,
                    file.Origin);
                RejectUnit(
                    entries,
                    source,
                    file,
                    plan.Members,
                    WorkflowArtifactRejectionKind.PersistenceFailure,
                    $"artifact '{member.Identity.ArtifactId}' could not be written to the executable store: {exception.Message}");
                return;
            }
        }

        // Activate dependencies-first so a parent is never live while a child's source reference is still absent —
        // that parent would dispatch into nothing. The plan's order, not the graph resolver's, carries this: the
        // resolver sorts by artifact id.
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in plan.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(await ActivateMemberAsync(source, file, member, unresolved, cancellationToken));
        }
    }

    private async ValueTask<WorkflowArtifactImportEntry> ActivateMemberAsync(
        IWorkflowArtifactReconciliationSource source,
        WorkflowArtifactClosureFile file,
        WorkflowExecutable member,
        HashSet<string> unresolved,
        CancellationToken cancellationToken)
    {
        var identity = member.Identity;

        var blockedBy = member.Dependencies
            .Select(dependency => dependency.ArtifactId)
            .FirstOrDefault(unresolved.Contains);
        if (blockedBy is not null)
        {
            unresolved.Add(identity.ArtifactId);
            return WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                WorkflowArtifactRejectionKind.ActivationFailure,
                $"dependency '{blockedBy}' did not become live, so activating this artifact would dispatch into nothing.");
        }

        var activationId = BuildActivationId(source.SourceId, identity);
        var slot = await activationAuthority.FindAsync(identity.DefinitionId, DefaultSlotName, cancellationToken);
        var command = new WorkflowActivationCommand(
            member,
            MintSourceReference(source, file.TenantId, identity, activationId),
            DefaultSlotName,
            activationId,
            WorkflowActivationSource.ArtifactReconciliation(source.SourceId),
            slot?.Revision ?? 0);

        WorkflowActivationResult result;
        try
        {
            result = await activationCoordinator.ActivateAsync(command, cancellationToken);
        }
        catch (WorkflowActivationException exception)
        {
            logger.LogError(
                exception,
                "Activation of imported artifact '{ArtifactId}' for definition '{DefinitionId}' could not be attempted.",
                identity.ArtifactId,
                identity.DefinitionId);
            unresolved.Add(identity.ArtifactId);
            return WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                WorkflowArtifactRejectionKind.ActivationFailure,
                exception.Message);
        }

        switch (result.Outcome)
        {
            case WorkflowActivationOutcome.Activated:
                logger.LogInformation(
                    "Imported artifact '{ArtifactId}' is now the live activation of definition '{DefinitionId}' slot '{SlotName}'.",
                    identity.ArtifactId,
                    identity.DefinitionId,
                    DefaultSlotName);
                return WorkflowArtifactImportEntry.Imported(
                    file.Origin, source.SourceId, identity.ArtifactId, identity.DefinitionId, identity.ArtifactVersion, activationId);

            case WorkflowActivationOutcome.AlreadyActive:
                return WorkflowArtifactImportEntry.AlreadyCurrent(
                    file.Origin,
                    source.SourceId,
                    identity.ArtifactId,
                    identity.DefinitionId,
                    identity.ArtifactVersion,
                    result.Slot.ActiveActivationId);

            case WorkflowActivationOutcome.Conflict:
                // The diagnostic already names the owning activation source — ownership comes from the slot's
                // Source field, never from parsing the id's `import:` prefix.
                logger.LogWarning(
                    "Imported artifact '{ArtifactId}' was refused for definition '{DefinitionId}': {Diagnostic}",
                    identity.ArtifactId,
                    identity.DefinitionId,
                    result.Diagnostic);
                unresolved.Add(identity.ArtifactId);
                return WorkflowArtifactImportEntry.Rejected(
                    file.Origin,
                    source.SourceId,
                    identity.ArtifactId,
                    identity.DefinitionId,
                    identity.ArtifactVersion,
                    WorkflowArtifactRejectionKind.ActivationConflict,
                    result.Diagnostic ?? "the activation slot transition was refused.");

            default:
                logger.LogError(
                    "Activation of imported artifact '{ArtifactId}' failed at step {FailedStep}: {Diagnostic}",
                    identity.ArtifactId,
                    result.FailedStep,
                    result.Diagnostic);
                unresolved.Add(identity.ArtifactId);
                return WorkflowArtifactImportEntry.Rejected(
                    file.Origin,
                    source.SourceId,
                    identity.ArtifactId,
                    identity.DefinitionId,
                    identity.ArtifactVersion,
                    WorkflowArtifactRejectionKind.ActivationFailure,
                    result.Diagnostic ?? $"the activation sequence failed at step {result.FailedStep}.");
        }
    }

    /// <summary>
    /// Mints the live source reference for one imported artifact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything identity-shaped is <b>copied</b> from the artifact, never derived: content-addressed ids are
    /// stable by design, and an importer that re-minted them would produce a different id for the same bytes on
    /// every engine — destroying the dedup the whole content-addressing scheme rests on.
    /// </para>
    /// <para>
    /// <c>Scope</c> is <c>Published</c> because the reference records the design-side event that produced the
    /// artifact, not how it got here; there is deliberately no <c>Activated</c> scope. The coordinator overwrites
    /// <c>SourceReferenceId</c>, <c>ActivationId</c> and <c>SlotId</c> with its own — it owns the
    /// activation↔reference link — so the values supplied here are the caller's provenance only.
    /// </para>
    /// </remarks>
    private WorkflowExecutableSourceReference MintSourceReference(
        IWorkflowArtifactReconciliationSource source,
        string? tenantId,
        WorkflowExecutableIdentity identity,
        string activationId)
    {
        var now = timeProvider.GetUtcNow();
        return new WorkflowExecutableSourceReference(
            SourceReferenceId: WorkflowActivationReferenceIdentity.Create(activationId),
            ArtifactId: identity.ArtifactId,
            SourceKind: source.SourceKind,
            SourceId: source.SourceId,
            SourceVersion: null,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: now,
            PublishedAt: now,
            Scope: WorkflowExecutableReferenceScope.Published,
            ActivationId: activationId,
            SlotId: WorkflowActivationSlotIdentity.Create(identity.DefinitionId, DefaultSlotName),
            TenantId: tenantId);
    }

    /// <summary>
    /// Recomputes the artifact's canonical content hash from the payload actually received and compares it against
    /// both the declared identity hash and the hash embedded in the content-addressed id. Returns the fault, or
    /// <see langword="null"/> when the artifact verifies.
    /// </summary>
    /// <remarks>
    /// An integrity guard, explicitly <b>not</b> tamper-proofing: the hasher is deterministic and public, so an
    /// attacker who can rewrite the payload can rewrite the hash too. What it does catch is the case that actually
    /// happens — a truncated, half-written or mis-merged closure file — before it becomes the permanent content of
    /// a content-addressed id. Signing remains the named follow-up.
    /// </remarks>
    private string? TryFindContentHashFault(WorkflowExecutable member)
    {
        var identity = member.Identity;

        if (member.InputContract is null)
            return $"artifact '{identity.ArtifactId}' carries no input contract, so its canonical content hash cannot be recomputed.";

        string recomputed;
        try
        {
            recomputed = hasher.ComputeHash(
                member.RootActivity,
                member.InputContract,
                member.Dependencies,
                member.CheckpointCadence,
                member.WorkflowVariables,
                member.IncidentStrategy);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"artifact '{identity.ArtifactId}' could not be hashed: {exception.Message}";
        }

        if (!StringComparer.Ordinal.Equals(recomputed, identity.ArtifactHash))
            return $"artifact '{identity.ArtifactId}' declares hash '{identity.ArtifactHash}' but its payload hashes to '{recomputed}'.";

        // The id embeds a prefix of the hash. A payload whose hash matches the declared hash but whose id does not
        // match either is an envelope that was rewritten inconsistently, and the id is what the store keys on.
        string expectedId;
        try
        {
            expectedId = hasher.CreateArtifactId(string.Empty, recomputed);
        }
        catch (ArgumentException exception)
        {
            return $"artifact '{identity.ArtifactId}' declares a hash this runtime cannot parse: {exception.Message}";
        }

        return identity.ArtifactId.EndsWith(expectedId, StringComparison.Ordinal)
            ? null
            : $"artifact id '{identity.ArtifactId}' does not embed the hash prefix '{expectedId}' its content hashes to.";
    }

    /// <summary>
    /// Cross-checks the trigger surface this runtime extracts from the payload against the one the envelope
    /// carries. Returns the fault, or <see langword="null"/> when they agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The carried bindings are <b>expectations, never rows</b>: the exporting engine's activation and slot ids are
    /// meaningless here, and the importer recomputes its own through the indexer during activation. All that is
    /// portable is the surface itself — which node emits which stimulus.
    /// </para>
    /// <para>
    /// An artifact the envelope carries <em>no</em> bindings for is treated as unasserted rather than as
    /// "asserted empty". A dependency member usually has no activation of its own on the exporting engine, and a
    /// trigger-free workflow legitimately has none either, so an empty carried set cannot distinguish "this has no
    /// triggers" from "this member's bindings were not exported". Rejecting on it would fail correct closures.
    /// </para>
    /// </remarks>
    private string? TryFindTriggerSurfaceFault(WorkflowExecutable member, WorkflowArtifactClosure closure)
    {
        var artifactId = member.Identity.ArtifactId;
        var carried = closure.TriggerBindings
            .Where(binding => StringComparer.Ordinal.Equals(binding.ArtifactId, artifactId))
            .Select(SurfaceKey)
            .ToHashSet(StringComparer.Ordinal);

        if (carried.Count == 0)
            return null;

        HashSet<string> recomputed;
        try
        {
            recomputed = triggerBindingExtractor.Evaluate(member).Bindings.Select(SurfaceKey).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is WorkflowTriggerPreflightException or WorkflowTriggerExtractionException)
        {
            return $"artifact '{artifactId}' declares triggers this runtime cannot materialize: {exception.Message}";
        }

        if (recomputed.SetEquals(carried))
            return null;

        var missing = carried.Except(recomputed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unexpected = recomputed.Except(carried, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return $"artifact '{artifactId}' has a trigger surface that disagrees with the closure's carried bindings — "
               + $"carried but not recomputed: [{string.Join(", ", missing)}]; recomputed but not carried: [{string.Join(", ", unexpected)}].";
    }

    /// <summary>The portable part of a binding: which node emits which stimulus. Activation and slot ids are not.</summary>
    private static string SurfaceKey(WorkflowTriggerBinding binding) =>
        $"{binding.ExecutableNodeId}|{binding.StimulusType}|{binding.StimulusHash}";

    /// <summary>
    /// Builds the deterministic activation id for one imported artifact.
    /// </summary>
    /// <remarks>
    /// Deterministic so that re-reconciling an unchanged mounted set produces the <em>same</em> activation id, and
    /// therefore the same source-reference id, making the whole pass an idempotent overwrite instead of an
    /// ever-growing pile of activations for one definition. The definition id is part of the key because two
    /// definitions with identical behaviour legitimately share one content-addressed artifact id. The
    /// <c>import:</c> prefix is for log readability only — ownership is decided by the slot's
    /// <c>WorkflowActivationSource</c> field and this string is never parsed.
    /// </remarks>
    private static string BuildActivationId(string sourceId, WorkflowExecutableIdentity identity) =>
        $"{ActivationIdPrefix}:{sourceId}:{identity.DefinitionId}:{identity.ArtifactId}";

    /// <summary>
    /// Records a rejection for every member of a unit that failed a gate. The unit has written nothing at this
    /// point, so no sibling is left half-imported — that is what makes the closure, rather than the artifact, the
    /// isolation unit.
    /// </summary>
    private void RejectUnit(
        List<WorkflowArtifactImportEntry> entries,
        IWorkflowArtifactReconciliationSource source,
        WorkflowArtifactClosureFile file,
        IReadOnlyList<WorkflowExecutable> members,
        WorkflowArtifactRejectionKind kind,
        string diagnostic)
    {
        logger.LogError(
            "Workflow artifact closure '{RootArtifactId}' from '{Origin}' was rejected ({Kind}): {Diagnostic}",
            file.Closure.RootArtifactId,
            file.Origin,
            kind,
            diagnostic);

        if (members.Count == 0)
        {
            entries.Add(WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                file.Closure.RootArtifactId,
                null,
                null,
                kind,
                diagnostic));
            return;
        }

        foreach (var member in members)
            entries.Add(WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                member.Identity.ArtifactId,
                member.Identity.DefinitionId,
                member.Identity.ArtifactVersion,
                kind,
                diagnostic));
    }
}
