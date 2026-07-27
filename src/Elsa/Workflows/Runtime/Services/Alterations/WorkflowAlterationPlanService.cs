using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Admits validated, protected alteration plans with tenant-scoped idempotency before target capture starts.</summary>
public sealed class WorkflowAlterationPlanService
{
    private readonly IWorkflowAlterationRegistry _registry;
    private readonly IWorkflowAlterationPayloadProtector _payloadProtector;
    private readonly IWorkflowAlterationStore _store;
    private readonly TimeProvider _timeProvider;

    public WorkflowAlterationPlanService(
        IWorkflowAlterationRegistry registry,
        IWorkflowAlterationPayloadProtector payloadProtector,
        IWorkflowAlterationStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(payloadProtector);
        ArgumentNullException.ThrowIfNull(store);

        _registry = registry;
        _payloadProtector = payloadProtector;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<WorkflowAlterationPlanAdmissionResult> SubmitAsync(
        WorkflowAlterationSubmission submission,
        WorkflowAlterationOperatorProvenance submittedBy,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submittedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ValidateSubmission(submission);

        var canonical = WorkflowAlterationRequestCanonicalizer.Canonicalize(submission);
        var canonicalHash = WorkflowAlterationRequestCanonicalizer.Hash(submission);
        var idempotencyKeyHash = HashIdempotencyKey(submission.AuthorityScope.TenantPartition, idempotencyKey);
        var planId = WorkflowAlterationIdentity.CreatePlanId(submission.AuthorityScope.TenantPartition, idempotencyKeyHash);
        var protectedPayload = _payloadProtector.Protect(planId, submission.AuthorityScope.TenantPartition, canonicalHash, canonical);
        var descriptors = submission.Alterations
            .Select(envelope => _registry.GetRequired(envelope.Kind, envelope.SchemaVersion))
            .ToArray();
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            planId,
            submission.AuthorityScope,
            submittedBy,
            idempotencyKeyHash,
            canonicalHash,
            protectedPayload,
            submission.Target,
            _timeProvider.GetUtcNow(),
            descriptors);

        return await _store.AdmitAsync(plan, cancellationToken);
    }

    /// <summary>
    /// Requests cooperative cancellation for a sealed cohort. An interrupted capture is terminalized without sealing
    /// or retaining provisional jobs. A running actor job is deliberately left to finish its checkpoint.
    /// </summary>
    public async ValueTask<WorkflowAlterationPlanState> CancelAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        var now = _timeProvider.GetUtcNow();
        var current = await _store.FindPlanAsync(planId, cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration plan '{planId}' was not found.");
        if (current.Status == WorkflowAlterationPlanStatus.CapturingTargets && current.SealedAt is null)
        {
            current = await _store.CancelUnsealedCaptureAsync(planId, now, cancellationToken);
            if (current.SealedAt is null || IsTerminal(current.Status))
                return current;
        }

        var plan = await _store.RequestCancellationAsync(planId, now, cancellationToken);
        if (plan.Status != WorkflowAlterationPlanStatus.Cancelling)
            return plan;

        if (plan.SealedAt is null)
            // Capture completed between the initial read and cancellation request. Re-evaluate using the terminal
            // unsealed transition instead of materializing a cohort after cancellation.
            return await _store.CancelUnsealedCaptureAsync(plan.PlanId, now, cancellationToken);

        var skippedOutcomes = CreateCancellationOutcomes(plan, now);
        await _store.CancelPendingJobsAsync(plan.PlanId, skippedOutcomes, now, cancellationToken);
        return await _store.ReconcileAsync(plan.PlanId, now, cancellationToken);
    }

    private void ValidateSubmission(WorkflowAlterationSubmission submission)
    {
        foreach (var envelope in submission.Alterations)
            _ = _registry.GetRequired(envelope.Kind, envelope.SchemaVersion);

        var cancelCount = submission.Alterations.Count(envelope => StringComparer.Ordinal.Equals(envelope.Kind, "CancelWorkflow"));
        if (cancelCount > 0 && submission.Alterations.Count != 1)
            throw new ArgumentException("CancelWorkflow must be the only alteration in a plan.", nameof(submission));

        var migrationIndexes = submission.Alterations
            .Select((envelope, index) => (envelope, index))
            .Where(item => StringComparer.Ordinal.Equals(item.envelope.Kind, "Migrate"))
            .Select(item => item.index)
            .ToArray();
        if (migrationIndexes.Length > 1 || (migrationIndexes.Length == 1 && migrationIndexes[0] != 0))
            throw new ArgumentException("Migrate may occur at most once and must be the first alteration.", nameof(submission));
    }

    private static string HashIdempotencyKey(string tenantPartition, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantPartition}\u001f{idempotencyKey}")));

    private static bool IsTerminal(WorkflowAlterationPlanStatus status) => status is
        WorkflowAlterationPlanStatus.Completed or WorkflowAlterationPlanStatus.CompletedWithFailures or
        WorkflowAlterationPlanStatus.Failed or WorkflowAlterationPlanStatus.Cancelled;

    private IReadOnlyCollection<WorkflowAlterationOutcome> CreateCancellationOutcomes(
        WorkflowAlterationPlanState plan,
        DateTimeOffset recordedAt)
    {
        var plaintext = _payloadProtector.Unprotect(
            plan.PlanId,
            plan.AuthorityScope.TenantPartition,
            plan.CanonicalRequestHash,
            plan.ProtectedPayload);
        using var document = JsonDocument.Parse(plaintext);
        return document.RootElement.GetProperty("alterations")
            .EnumerateArray()
            .Select((item, ordinal) => new WorkflowAlterationOutcome(
                ordinal,
                item.GetProperty("kind").GetString()!,
                item.GetProperty("schemaVersion").GetInt32(),
                WorkflowAlterationOutcomeStatus.Skipped,
                "PlanCancelled",
                "Not applied because the plan was cancelled before this target entered actor execution.",
                recordedAt))
            .ToArray();
    }
}
