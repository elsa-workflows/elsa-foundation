using Elsa.Activities.Runtime.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Models;
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
/// <b>The isolation unit is the closure, and isolation across the mounted set is per unit.</b> Any member failing
/// any gate rejects every member of its unit and writes nothing at all — no sibling persistence, no partial
/// import — while every other unit in the batch is still reconciled. That is why a rejection is a diagnostic on
/// the pass result rather than a thrown exception: one broken export must not be able to take down the deploy.
/// </para>
/// <para>
/// <b>Activation is one call.</b> The importer never takes a lease, writes a projection, notifies an observer or
/// compensates — <see cref="IWorkflowActivationCoordinator"/> owns that entire sequence for every path, and a
/// second copy of it here would be exactly the duplicated authority FR-B-006 exists to remove. The importer's
/// recovery unit is the next reconcile pass.
/// </para>
/// <para>
/// <b>Reconciliation is a seed, not an enforcer (T118).</b> It requests activation with no takeover intent, so a
/// definition whose slot a different source owns is refused and skipped — permanently, pass after pass. This is
/// deliberately <em>not</em> the usual declarative-controller precedence: a controller that re-asserts over
/// imperative drift runs continuously, whereas this runs at boot and shell reload, so re-asserting would mean the
/// next reload silently reverting an operator's explicit publish. The skip is named and surfaced at boot instead,
/// because a mount being ignored for a definition is invisible from every other angle.
/// </para>
/// </remarks>
public sealed class WorkflowArtifactReconciler(
    IEnumerable<IWorkflowArtifactReconciliationSource> sources,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableHasher hasher,
    IRuntimeRequirementChecker requirementChecker,
    IEnumerable<IActivityActivationStrategy> activationStrategies,
    IWorkflowTriggerBindingExtractor triggerBindingExtractor,
    IWorkflowActivationAuthority activationAuthority,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowActivationCoordinator activationCoordinator,
    IArtifactForeignOwnerPolicy foreignOwnerPolicy,
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

    /// <summary>
    /// Every installed activation strategy indexed by the consumer key <c>ActivityActivator</c> dispatches on.
    /// </summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _schemasByConsumerKey = activationStrategies
        .GroupBy(strategy => strategy.ConsumerKey, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyCollection<string>)group
                .SelectMany(strategy => strategy.SupportedSchemaVersions)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

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
    /// Per-input read failures are yielded as entries and do not stop later inputs. A custom source that predates
    /// that result shape and still throws <see cref="InvalidWorkflowArtifactClosureException"/> remains contained,
    /// but its iterator necessarily ends. A pass-aborting <see cref="WorkflowArtifactReconciliationException"/>
    /// deliberately propagates: a mount that is not there is not the same as a mount that is empty.
    /// </remarks>
    private async ValueTask ReconcileSourceAsync(
        IWorkflowArtifactReconciliationSource source,
        List<WorkflowArtifactImportEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var file in source.ReadAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                var readError = file.ReadError;
                if (readError is not null || file.Closure is null)
                {
                    var diagnostic = readError?.Message ?? $"workflow artifact closure at '{file.Origin}' was empty.";
                    entries.Add(WorkflowArtifactImportEntry.Rejected(
                        file.Origin,
                        source.SourceId,
                        string.Empty,
                        null,
                        null,
                        WorkflowArtifactRejectionKind.MalformedClosure,
                        diagnostic));
                    continue;
                }

                await ReconcileUnitAsync(source, file, entries, cancellationToken);
            }
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
        var closure = file.Closure!;

        // Gate 1+2 — the envelope validates against itself alone: every dependency edge resolves inside the
        // carried set, declared hashes agree, no duplicate identities, no cycles.
        var plan = WorkflowArtifactClosurePlanner.Plan(closure);
        if (!plan.IsValid)
        {
            RejectUnit(entries, source, file, plan.Members, plan.RejectionKind, plan.Diagnostic!);
            return;
        }

        // Gate 2a — imported provenance must describe a durable published artifact. The closure's references are
        // expectations only and are never copied into this engine, but accepting a test-run or draft reference
        // would still let a non-publishable snapshot be relabelled as the Published reference minted below.
        if (TryFindProvenanceFault(closure, plan.Members) is { } provenanceFault)
        {
            RejectUnit(entries, source, file, plan.Members, WorkflowArtifactRejectionKind.MalformedClosure, provenanceFault);
            return;
        }

        // Gate 2 (continued) — every member's ArtifactVersion must be orderable, and no two members may claim one
        // logical (DefinitionId, ArtifactVersion) with different content. Both are envelope-self facts like the
        // planner's, but they live here beside the other per-member gates rather than in the planner, which owns
        // the dependency graph alone.
        if (TryFindVersionFault(plan.Members) is { } versionFault)
        {
            RejectUnit(entries, source, file, plan.Members, versionFault.Kind, versionFault.Diagnostic);
            return;
        }

        // Gate 2b — recompute every member's canonical content hash BEFORE anything persists. The executable
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

        // Gate 3 — the executability gate (FR-B-005a). Every member must be runnable by *this* engine before any
        // member of the unit is written: this is the whole reason US2 exists, and running it after a sibling had
        // been persisted would leave the store holding artifacts for a unit that never activates.
        foreach (var member in plan.Members)
        {
            if (TryFindRequirementFault(member) is { } fault)
            {
                RejectUnit(entries, source, file, plan.Members, WorkflowArtifactRejectionKind.UnmetRequirement, fault);
                return;
            }
        }

        // Gate 3b — the carried trigger surface is an expectation to check, never rows to import. A disagreement
        // between what the exporter said the artifact's triggers are and what this runtime extracts from the very
        // same payload means the two sides do not agree on what the artifact does. It runs *after* the
        // executability gate so an artifact this engine simply cannot run is named as such, rather than as a
        // trigger-materialization failure downstream of the same missing package.
        foreach (var member in plan.Members)
        {
            if (TryFindTriggerSurfaceFault(member, closure) is { } fault)
            {
                RejectUnit(entries, source, file, plan.Members, WorkflowArtifactRejectionKind.TriggerSurfaceMismatch, fault);
                return;
            }
        }

        // Persist the entire closure through the store's atomic batch boundary. Reconciliation's isolation unit is
        // the closure, so a provider that cannot make every previously absent member visible together fails closed
        // rather than degrading to sequential writes and leaving an orphaned prefix after a storage fault.
        try
        {
            await executableStore.SaveBatchAsync(plan.Members, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Workflow artifact closure from '{Origin}' could not be persisted atomically.",
                file.Origin);
            RejectUnit(
                entries,
                source,
                file,
                plan.Members,
                WorkflowArtifactRejectionKind.PersistenceFailure,
                $"the closure could not be written atomically to the executable store: {exception.Message}");
            return;
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

        // Steps 4 and 5 — idempotency and latest-wins supersession, both answered from the activation the slot
        // already serves. This is the one place the importer consults engine state to decide, which is why it sits
        // on this side of the planner's envelope-only line.
        var active = await FindActiveReferenceAsync(slot, cancellationToken);

        var candidateSource = WorkflowActivationSource.ArtifactReconciliation(source.SourceId);
        var foreignOwnerMustBeEvaluated = slot?.ActiveActivationId is not null &&
            slot.Source is { } incumbent &&
            !incumbent.IsSameOwnerAs(candidateSource) &&
            !IsSameArtifactActivation(active, file.TenantId, identity.ArtifactId);

        // Ownership is authoritative even when the incumbent's version would otherwise make this candidate look
        // old or like a same-version hash mismatch. Let the coordinator attempt that foreign transition first so
        // its conflict/compensation path remains the single place that refuses the slot and the policy is called
        // only after the refusal. The coordinator's documented same-artifact no-op remains available below.
        if (!foreignOwnerMustBeEvaluated && active is not null && TryResolveSupersession(identity, active) is { } supersession)
        {
            // A None kind is the verdict's "not a rejection" arm: importable, deliberately not activated.
            if (supersession.Kind == WorkflowArtifactRejectionKind.None)
            {
                logger.LogInformation(
                    "Imported artifact '{ArtifactId}' was skipped for definition '{DefinitionId}': {Diagnostic}",
                    identity.ArtifactId,
                    identity.DefinitionId,
                    supersession.Diagnostic);
                // Deliberately NOT recorded as unresolved: the definition is live on an equal-or-newer activation,
                // so a dependent that dispatches into it is not dispatching into nothing. The skipped artifact
                // itself stays in the store as an unreferenced content-addressed blob — reclaimable by reference
                // GC, and already there for the pass that does activate it.
                return WorkflowArtifactImportEntry.Skipped(
                    file.Origin,
                    source.SourceId,
                    identity.ArtifactId,
                    identity.DefinitionId,
                    identity.ArtifactVersion,
                    WorkflowArtifactSkipReason.OlderVersion,
                    supersession.Diagnostic);
            }

            logger.LogError(
                "Imported artifact '{ArtifactId}' was rejected for definition '{DefinitionId}' ({Kind}): {Diagnostic}",
                identity.ArtifactId,
                identity.DefinitionId,
                supersession.Kind,
                supersession.Diagnostic);
            unresolved.Add(identity.ArtifactId);
            return WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                supersession.Kind,
                supersession.Diagnostic);
        }

        var command = new WorkflowActivationCommand(
            member,
            MintSourceReference(source, file.TenantId, identity, activationId),
            DefaultSlotName,
            activationId,
            candidateSource,
            slot?.Revision ?? 0);

        WorkflowActivationResult result;
        try
        {
            result = await activationCoordinator.ActivateAsync(command, cancellationToken);
        }
        // Deliberately the whole family, not WorkflowActivationException alone. The authority and the coordinator
        // throw siblings rather than subtypes -- GroundworkWorkflowActivationAuthorityException comes from the
        // provider, PublicationActivationException from the publishing side of the shared coordinator, and the
        // root-write lease exceptions are rethrown unwrapped -- so a narrow catch lets any of them escape the pass
        // and fail shell activation. That is precisely what this class promises cannot happen: one broken export
        // must not take down the deploy. Cancellation still propagates; a per-artifact fault stays per-artifact.
        catch (Exception exception) when (exception is not OperationCanceledException)
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

            case WorkflowActivationOutcome.Conflict when result.Conflict == WorkflowActivationConflict.ForeignSource:
                return await ResolveForeignOwnerAsync(
                    source,
                    file,
                    identity,
                    command.Source,
                    result.Slot,
                    result.Diagnostic,
                    cancellationToken);

            case WorkflowActivationOutcome.Conflict:
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

    private async ValueTask<WorkflowArtifactImportEntry> ResolveForeignOwnerAsync(
        IWorkflowArtifactReconciliationSource source,
        WorkflowArtifactClosureFile file,
        WorkflowExecutableIdentity identity,
        WorkflowActivationSource candidateSource,
        WorkflowActivationSlot slot,
        string? transitionDiagnostic,
        CancellationToken cancellationToken)
    {
        // T118: another activation source owns the slot, and reconciliation never takes it back. The coordinator
        // has already refused this transition, whether the refusal was the deliberate pre-ordering ownership check
        // or a race discovered after the initial read, so this method only turns that refusal into the report entry.
        var incumbent = slot.Source;
        var owner = incumbent?.Describe() ?? "another activation source";
        var ownership =
            $"definition '{identity.DefinitionId}' slot '{DefaultSlotName}' is owned by activation source "
            + $"'{owner}', which claimed it explicitly. Reconciliation never reclaims a slot another source owns, "
            + "so this mounted artifact is ignored for this definition until that source releases the slot. "
            + $"{transitionDiagnostic}".TrimEnd();

        // T124 — the policy chooses how loudly the refusal is reported, never whether the slot is taken. A slot
        // conflict with no owner recorded is an authority defect rather than a question for a policy, so it takes
        // the safe default without asking.
        var decision = incumbent is null
            ? ArtifactForeignOwnerDecision.Skip
            : await foreignOwnerPolicy.DecideAsync(
                new ArtifactForeignOwnerContext(identity.DefinitionId, incumbent, candidateSource),
                cancellationToken);

        if (ReferenceEquals(decision, ArtifactForeignOwnerDecision.Reject))
        {
            logger.LogError(
                "Imported artifact '{ArtifactId}' from '{Origin}' was NOT activated for definition '{DefinitionId}': the "
                + "'{SlotName}' activation slot is owned by activation source '{Owner}', and this engine's foreign-owner "
                + "policy treats that as a rejection.",
                identity.ArtifactId,
                file.Origin,
                identity.DefinitionId,
                DefaultSlotName,
                owner);
        }
        else
        {
            logger.LogWarning(
                "Imported artifact '{ArtifactId}' from '{Origin}' was NOT activated for definition '{DefinitionId}': the "
                + "'{SlotName}' activation slot is owned by activation source '{Owner}'. Reconciliation never reclaims a "
                + "slot another source owns, so updating the mounted artifact will not change what this definition serves "
                + "until that source releases it. Diagnostic: {Diagnostic}",
                identity.ArtifactId,
                file.Origin,
                identity.DefinitionId,
                DefaultSlotName,
                owner,
                transitionDiagnostic);
        }

        // Deliberately NOT recorded as unresolved on either answer: the definition IS live on the owner's
        // activation, so a dependent that dispatches into it is not dispatching into nothing.
        return ForeignOwnerEntry(decision, file, source, identity, ownership);
    }

    private static bool IsSameArtifactActivation(
        WorkflowExecutableSourceReference? active,
        string? tenantId,
        string candidateArtifactId) =>
        active is { DeletedAt: null } &&
        StringComparer.Ordinal.Equals(active.ArtifactId, candidateArtifactId) &&
        StringComparer.Ordinal.Equals(active.TenantId, tenantId);

    /// <summary>
    /// Turns an <see cref="IArtifactForeignOwnerPolicy"/> answer into the pass entry that reports it.
    /// </summary>
    /// <remarks>
    /// <b><see langword="static"/> on purpose, and load-bearing (T124).</b> A static method cannot touch this
    /// class's injected <c>IWorkflowActivationCoordinator</c> or <c>IWorkflowActivationAuthority</c>, so the whole
    /// route from "a host's policy answered" to "something got activated" is one the compiler refuses rather than
    /// one a comment discourages. Both answers produce an entry that did not activate; the slot is already
    /// untouched by the time either is built.
    /// </remarks>
    private static WorkflowArtifactImportEntry ForeignOwnerEntry(
        ArtifactForeignOwnerDecision decision,
        WorkflowArtifactClosureFile file,
        IWorkflowArtifactReconciliationSource source,
        WorkflowExecutableIdentity identity,
        string diagnostic) =>
        ReferenceEquals(decision, ArtifactForeignOwnerDecision.Reject)
            ? WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                WorkflowArtifactRejectionKind.ActivationConflict,
                diagnostic)
            : WorkflowArtifactImportEntry.Skipped(
                file.Origin,
                source.SourceId,
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                WorkflowArtifactSkipReason.ForeignSlotOwner,
                diagnostic);

    /// <summary>
    /// The live source reference the definition's slot currently serves, or <see langword="null"/> when nothing is
    /// activated, the reference is gone, or it has been retired.
    /// </summary>
    /// <remarks>
    /// A retired reference deliberately reads as "nothing active". It means the last activation attempt was
    /// compensated (<c>"activation-failed"</c>) or superseded, so the version it carries is not a serving version
    /// and must not be able to hold a repair pass back — which is exactly the crashed-half-import case whose
    /// recovery unit is the next pass.
    /// </remarks>
    private async ValueTask<WorkflowExecutableSourceReference?> FindActiveReferenceAsync(
        WorkflowActivationSlot? slot,
        CancellationToken cancellationToken)
    {
        if (slot?.ActiveActivationId is not { } activeActivationId)
            return null;

        var reference = await sourceReferenceStore.FindAsync(
            WorkflowActivationReferenceIdentity.Create(activeActivationId),
            cancellationToken);

        return reference is { DeletedAt: null } ? reference : null;
    }

    /// <summary>
    /// Latest-wins (FR-B-007). Returns <see langword="null"/> when the candidate should be handed to the
    /// coordinator, a <c>None</c>-kind verdict when it must be skipped as not-newer, and a rejection kind when the
    /// two sides claim one logical version with different content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The active version is read, never stored.</b> It comes off the minted source reference the active
    /// activation resolves to, which already carries <c>ArtifactVersion</c> — so latest-wins introduces no state of
    /// its own and cannot drift from what is actually serving.
    /// </para>
    /// <para>
    /// Ordering is <see cref="SemVer.ToSortKey"/> plus an ordinal compare: byte-for-byte the comparator the
    /// design-side <c>WorkflowsVersionReconciler</c> and the design version store use, so a design engine and a
    /// runtime engine given the same two versions always pick the same winner. Build metadata is outside the sort
    /// key, which is what makes <c>1.0.0+a</c> and <c>1.0.0+b</c> the <em>same</em> logical version — and therefore
    /// a broken source when their content differs.
    /// </para>
    /// <para>
    /// An active version this runtime cannot parse is <b>not</b> treated as a block. It cannot come from this
    /// importer (gate 2b refuses unorderable versions before anything is written), so it belongs to another
    /// activation source — and a foreign-owned slot is refused by the coordinator's ownership rule, which produces
    /// a named <see cref="WorkflowArtifactSkipReason.ForeignSlotOwner"/> entry naming the owner instead of a
    /// silent skip.
    /// </para>
    /// </remarks>
    private (WorkflowArtifactRejectionKind Kind, string Diagnostic)? TryResolveSupersession(
        WorkflowExecutableIdentity candidate,
        WorkflowExecutableSourceReference active)
    {
        if (!SemVer.TryParse(candidate.ArtifactVersion, out var candidateVersion))
            return null; // Unreachable: gate 2b already refused the unit. Defensive only.

        if (!SemVer.TryParse(active.ArtifactVersion, out var activeVersion))
        {
            logger.LogWarning(
                "Definition '{DefinitionId}' serves activation '{ActivationId}' at version '{ActiveVersion}', which is not a "
                + "semantic version; latest-wins cannot order artifact '{ArtifactId}' against it, so the activation authority decides.",
                candidate.DefinitionId,
                active.ActivationId,
                active.ArtifactVersion,
                candidate.ArtifactId);
            return null;
        }

        var comparison = string.CompareOrdinal(candidateVersion.ToSortKey(), activeVersion.ToSortKey());

        if (comparison > 0)
            return null; // Newer: supersede.

        if (comparison < 0)
            return (
                WorkflowArtifactRejectionKind.None,
                $"definition '{candidate.DefinitionId}' already serves version '{active.ArtifactVersion}' "
                + $"(artifact '{active.ArtifactId}'), which sorts above this artifact's '{candidate.ArtifactVersion}'. "
                + "Activation never moves backward onto an older artifact.");

        // Equal logical version. Same content is the idempotent no-op the coordinator already answers; different
        // content is a broken source.
        if (StringComparer.Ordinal.Equals(active.ArtifactId, candidate.ArtifactId))
            return null;

        return (
            WorkflowArtifactRejectionKind.VersionHashMismatch,
            DescribeVersionHashMismatch(
                candidate.DefinitionId,
                active.ArtifactVersion,
                active.ArtifactId,
                candidate.ArtifactVersion,
                candidate.ArtifactId,
                candidate.ArtifactHash));
    }

    /// <summary>
    /// Gate 2a: a closure may carry only durable Published provenance, and every executable must retain a
    /// non-draft definition-version id before this importer mints a new Published reference for it.
    /// </summary>
    private static string? TryFindProvenanceFault(
        WorkflowArtifactClosure closure,
        IReadOnlyList<WorkflowExecutable> members)
    {
        foreach (var member in members)
        {
            var identity = member.Identity;
            if (IsDraftOrMissingDefinitionVersion(identity.DefinitionVersionId))
                return
                    $"artifact '{identity.ArtifactId}' of definition '{identity.DefinitionId}' carries draft or missing "
                    + $"definition-version provenance '{identity.DefinitionVersionId}'. Only non-draft published artifacts "
                    + "can be imported.";
        }

        foreach (var reference in closure.SourceReferences)
        {
            if (reference is null)
                return "the closure carries a null source reference; published provenance must be complete.";

            if (reference.Scope != WorkflowExecutableReferenceScope.Published)
                return
                    $"source reference '{reference.SourceReferenceId}' carries scope '{reference.Scope}'. Only Published "
                    + "source-reference provenance can be imported.";

            if (IsDraftOrMissingDefinitionVersion(reference.DefinitionVersionId))
                return
                    $"source reference '{reference.SourceReferenceId}' carries draft or missing definition-version "
                    + $"provenance '{reference.DefinitionVersionId}'. Only non-draft published artifacts can be imported.";
        }

        return null;
    }

    private static bool IsDraftOrMissingDefinitionVersion(string? definitionVersionId) =>
        string.IsNullOrWhiteSpace(definitionVersionId) ||
        definitionVersionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gate 2b: every member must declare an orderable <c>ArtifactVersion</c>, and one logical
    /// <c>(DefinitionId, ArtifactVersion)</c> must not be claimed twice with different content.
    /// </summary>
    /// <remarks>
    /// Orderability is a hard import requirement rather than a warning because latest-wins is the only thing
    /// standing between a redeploy and an activation moving backward: an unorderable version cannot be compared to
    /// what is serving, so admitting one would mean either refusing every later import for that definition or
    /// activating blind. Refusing at the door names the problem where it can be fixed — in the exporter.
    /// </remarks>
    private static (WorkflowArtifactRejectionKind Kind, string Diagnostic)? TryFindVersionFault(
        IReadOnlyList<WorkflowExecutable> members)
    {
        var claimed = new Dictionary<(string DefinitionId, string SortKey), WorkflowExecutableIdentity>();

        foreach (var member in members)
        {
            var identity = member.Identity;

            if (!SemVer.TryParse(identity.ArtifactVersion, out var version))
                return (
                    WorkflowArtifactRejectionKind.UnorderableVersion,
                    $"artifact '{identity.ArtifactId}' of definition '{identity.DefinitionId}' declares version "
                    + $"'{identity.ArtifactVersion}', which is not a semantic version. Latest-wins supersession orders "
                    + "activations by SemVer precedence, so an unorderable version cannot be imported.");

            var key = (identity.DefinitionId, version.ToSortKey());
            if (!claimed.TryGetValue(key, out var incumbent))
            {
                claimed.Add(key, identity);
                continue;
            }

            if (!StringComparer.Ordinal.Equals(incumbent.ArtifactId, identity.ArtifactId))
                return (
                    WorkflowArtifactRejectionKind.VersionHashMismatch,
                    DescribeVersionHashMismatch(
                        identity.DefinitionId,
                        incumbent.ArtifactVersion,
                        incumbent.ArtifactId,
                        identity.ArtifactVersion,
                        identity.ArtifactId,
                        identity.ArtifactHash,
                        incumbent.ArtifactHash));
        }

        return null;
    }

    /// <summary>
    /// The broken-source message, shaped after the design side's <c>ActivityVersionHashMismatchException</c> so the
    /// two reconcilers describe the same breakage the same way.
    /// </summary>
    private static string DescribeVersionHashMismatch(
        string definitionId,
        string persistedVersion,
        string persistedArtifactId,
        string incomingVersion,
        string incomingArtifactId,
        string incomingHash,
        string? persistedHash = null) =>
        $"workflow definition '{definitionId}' already claims version {persistedVersion} through artifact "
        + $"'{persistedArtifactId}'{(persistedHash is null ? string.Empty : $" (hash '{persistedHash}')")}, but the same logical version "
        + $"({incomingVersion}) is claimed by artifact '{incomingArtifactId}' with hash '{incomingHash}'. "
        + "Same logical identity must imply same content — build metadata is ignored when matching versions. "
        + "The source is broken: fix the exporting projection or bump the artifact version.";

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
    /// The import gate (FR-B-005a): can <em>this</em> engine actually run this artifact? Returns one diagnostic
    /// naming everything that is missing, or <see langword="null"/> when the artifact is executable here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three axes, evaluated together and reported together. A partial answer ("your activity package is missing")
    /// that hides a second problem ("…and so is your storage driver") costs the operator a second deploy cycle to
    /// discover, so every fault is collected before the unit is refused.
    /// </para>
    /// <para>
    /// Axes (a) declared consumer/schema requirements and (b) per-node CLR activity-type presence come from the
    /// shared <see cref="IRuntimeRequirementChecker"/>, which publishing's deployment preflight uses too — one
    /// definition of "executable here" for both callers.
    /// </para>
    /// <para>
    /// Axis (c) — <see cref="TryFindUnresolvableConsumerFault"/> — is import-only and lives here rather than in the
    /// shared checker. It asks whether each node's descriptor consumer key resolves to an installed
    /// <see cref="IActivityActivationStrategy"/>, which is the registry <c>ActivityActivator</c> literally
    /// dispatches on. The shared checker deliberately reads the <em>advertised</em>
    /// <c>IRuntimeActivityConsumerCapability</c> set instead: it is a singleton description, whereas the strategies
    /// are scoped execution-path services, and publishing's preflight is an advisory read model whose diagnostic
    /// vocabulary has no code for a per-node activation-strategy fault. Only the importer admits artifacts to
    /// execution, so only the importer needs the stronger question.
    /// </para>
    /// </remarks>
    private string? TryFindRequirementFault(WorkflowExecutable member)
    {
        var faults = new List<string>();

        if (TryFindUnresolvableConsumerFault(member) is { } consumerFault)
            faults.Add(consumerFault);

        var verdict = requirementChecker.Check(RuntimeRequirementCheckSubject.FromExecutable(member));

        foreach (var requirement in verdict.Requirements.Where(entry => entry.Status != RuntimeRequirementStatus.Available))
        {
            faults.Add(requirement.Status == RuntimeRequirementStatus.UnsupportedSchema
                ? $"activity consumer '{requirement.ConsumerKey}' is installed but does not support descriptor schema "
                  + $"'{requirement.SchemaVersion}' (supported: [{string.Join(", ", requirement.SupportedSchemaVersions)}])"
                : $"activity consumer '{requirement.ConsumerKey}' schema '{requirement.SchemaVersion}' is not installed on this runtime");
        }

        foreach (var driver in verdict.StorageDrivers.Where(entry => entry.Status != RuntimeRequirementStatus.Available))
            faults.Add($"durable-value storage driver '{driver.DriverKey}' is not installed on this runtime");

        foreach (var activityType in verdict.ActivityTypes.Where(entry => entry.Status != RuntimeRequirementStatus.Available))
        {
            var nodes = string.Join(", ", activityType.NodeIds);
            faults.Add(string.IsNullOrEmpty(activityType.TypeAlias)
                ? $"node(s) [{nodes}] carry a CLR activity descriptor this runtime cannot read"
                : $"activity type '{activityType.TypeAlias}' is not registered on this runtime (used by node(s) [{nodes}])");
        }

        return faults.Count == 0
            ? null
            : $"artifact '{member.Identity.ArtifactId}' cannot be executed by this runtime: {string.Join("; ", faults)}.";
    }

    /// <summary>
    /// Axis (c): every non-intrinsic node's descriptor consumer key must resolve to an installed activation
    /// strategy at the node's descriptor schema version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes a gap the two declared axes do not: a node whose <c>DescriptorType</c> carries the descriptor's
    /// <em>CLR type name</em> instead of the consumer key (<c>elsa.clr-activity</c>) parses, validates, hashes to
    /// its own content-addressed id, activates cleanly — and then faults on first execution with
    /// <c>UnknownActivityConsumerException</c>, exactly the production-time surprise US2 exists to prevent. The
    /// importer cannot repair it either: a content-addressed artifact is never rewritten, because the bytes are
    /// the identity.
    /// </para>
    /// <para>
    /// Intrinsics are skipped: they carry the reserved <c>"intrinsic"</c> descriptor type, are executed by the
    /// engine's own intrinsic executor and never reach the activation-strategy registry at all.
    /// </para>
    /// </remarks>
    private string? TryFindUnresolvableConsumerFault(WorkflowExecutable member)
    {
        var faults = new List<string>();

        foreach (var node in member.Nodes.Where(node => node.IntrinsicKind is null))
        {
            if (!_schemasByConsumerKey.TryGetValue(node.DescriptorType, out var schemas))
            {
                faults.Add($"node '{node.ExecutableNodeId}' declares descriptor consumer '{node.DescriptorType}', "
                           + "which no activity activation strategy installed on this runtime handles");
                continue;
            }

            if (!schemas.Contains(node.DescriptorSchemaVersion, StringComparer.Ordinal))
                faults.Add($"node '{node.ExecutableNodeId}' declares descriptor consumer '{node.DescriptorType}' at schema "
                           + $"'{node.DescriptorSchemaVersion}', which its activation strategy does not support "
                           + $"(supported: [{string.Join(", ", schemas)}])");
        }

        return faults.Count == 0 ? null : string.Join("; ", faults.Order(StringComparer.Ordinal));
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
        $"{ActivationIdPrefix}:{sourceId.Length}:{sourceId}:{identity.DefinitionId.Length}:{identity.DefinitionId}:"
        + $"{identity.ArtifactId.Length}:{identity.ArtifactId}";

    /// <summary>
    /// Records a rejection for every member of a unit that failed a gate, as a named diagnostic on the pass result
    /// <b>and</b> a log entry — never as a thrown exception, which is what keeps one bad unit from failing the batch.
    /// </summary>
    /// <remarks>
    /// Every member is listed, not just the one that failed, because the closure is the isolation unit: none of them
    /// were imported and an operator reading the result needs to see that. The persistence boundary is atomic, so
    /// this remains true for storage failures as well as validation failures.
    /// </remarks>
    private void RejectUnit(
        List<WorkflowArtifactImportEntry> entries,
        IWorkflowArtifactReconciliationSource source,
        WorkflowArtifactClosureFile file,
        IReadOnlyList<WorkflowExecutable> members,
        WorkflowArtifactRejectionKind kind,
        string diagnostic)
    {
        var closure = file.Closure ?? throw new InvalidOperationException("A readable closure is required to reject a reconciliation unit.");
        logger.LogError(
            "Workflow artifact closure '{RootArtifactId}' from '{Origin}' was rejected ({Kind}): {Diagnostic}",
            closure.RootArtifactId,
            file.Origin,
            kind,
            diagnostic);

        if (members.Count == 0)
        {
            entries.Add(WorkflowArtifactImportEntry.Rejected(
                file.Origin,
                source.SourceId,
                closure.RootArtifactId,
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
