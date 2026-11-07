using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.AspNetCore.DataProtection;
using static Elsa.Workflows.Runtime.Api.StandaloneTriggers.TriggerManagement;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

/// <summary>Authorized read/control projection over the selected actual source provider; owns no delivery storage.</summary>
internal sealed class TriggerDeliveryOperations(IStandaloneTriggerStore resources, ITriggerRequestAccess access,
    IStandaloneTriggerTargetDiscovery targets, IDataProtectionProvider protection, TimeProvider time,
    IRuntimeIngressDeliveryStore? ingress = null, IWorkflowExecutionStateStore? executions = null, IRuntimeModelExecutionReader? models = null)
{
    private readonly IDataProtector previews = protection.CreateProtector("Elsa.StandaloneTriggers.DeliveryPreview.v1");
    private readonly IDataProtector cursors = protection.CreateProtector("Elsa.StandaloneTriggers.DeliveryCursor.v1");
    internal bool SupportsInspection => ingress?.SupportsInspection == true;
    internal bool SupportsControls => ingress?.SupportsControls == true;

    internal async ValueTask<TriggerPage<TriggerDeliveryView>> ListAsync(string? triggerId, int limit, string? after, CancellationToken ct)
    {
        RequireInspection();
        if (limit is < 1 or > 100) throw Invalid("Page size must be between 1 and 100.");
        if (triggerId is not null) _ = await RequireResourceAsync(triggerId, TriggerPermissions.DeliveriesRead, ct);
        TriggerDeliveryCursor cursor = after is null ? new(access.Subject, access.Scope, triggerId, null) : ReadCursor(after, triggerId);
        var output = new List<TriggerDeliveryView>();
        var sourceAfter = cursor.SourceAfter;
        for (var scanned = 0; scanned < 100; scanned++)
        {
            RuntimePostCommitOutboxItem? source;
            if (scanned == 0 && cursor.SourceId is not null)
                source = await ingress!.FindDeliveryAsync(access.Scope, cursor.SourceId, ct);
            else
            {
                var page = await ingress!.ListDeliveriesAsync(access.Scope, new(triggerId, 1, sourceAfter), ct);
                source = page.Items.SingleOrDefault();
                if (source is null && page.Next is not null) { sourceAfter = page.Next; continue; }
            }
            if (source is null)
            {
                if (scanned == 0 && cursor.SourceId is not null) { sourceAfter = cursor.SourceId; continue; }
                return new(output, null);
            }
            foreach (var owner in ResourceIds(source).Where(id => triggerId is null || id == triggerId))
            {
                if (scanned == 0 && cursor.SourceId == source.OutboxItemId && cursor.AfterTrigger is not null && StringComparer.Ordinal.Compare(owner, cursor.AfterTrigger) <= 0) continue;
                var resource = await resources.FindAsync(access.Scope, owner, ct);
                if (resource is null || !await access.HasAsync(TriggerPermissions.DeliveriesRead, resource.Trigger, ct)) continue;
                output.Add(await ViewAsync(source, resource.Trigger, ct));
                if (output.Count == limit)
                    return new(output, ProtectCursor(new(access.Subject, access.Scope, triggerId, sourceAfter, source.OutboxItemId, owner)));
            }
            sourceAfter = source.OutboxItemId;
        }
        return new(output, ProtectCursor(new(access.Subject, access.Scope, triggerId, sourceAfter)));
    }

    internal async ValueTask<TriggerDeliveryView> ReadAsync(string id, CancellationToken ct)
    {
        RequireInspection();
        var (source, owner) = await ResolveAsync(id, null, ct);
        var resource = await RequireResourceAsync(owner, TriggerPermissions.DeliveriesRead, ct);
        return await ViewAsync(source, resource.Trigger, ct);
    }

    internal async ValueTask<TriggerPreview> PreviewAsync(string triggerId, JsonElement request, CancellationToken ct)
    {
        RequireControls();
        var command = request.GetProperty("command"); var kind = Kind(command);
        var resource = await RequireResourceAsync(triggerId, Permission(kind), ct);
        if (Number(request, "expectedRevision") != resource.Trigger.Revision) throw Changed();
        var (source, _) = await ResolveAsync(Text(command, "deliveryId"), triggerId, ct);
        var expected = Select(source, triggerId, command);
        var expires = time.GetUtcNow().AddMinutes(5);
        var proof = new TriggerDeliveryPreviewProof(access.Subject, access.Scope, triggerId, resource.Trigger.Revision,
            source.OutboxItemId, source.SourceFingerprint, source.SubscriptionDelivery!.SelectionFingerprint, Fingerprint(command),
            Revision(source, triggerId), expected, expires);
        var sealedCount = expected.Count(item => source.SubscriptionDelivery.Progress.Single(progress => progress.RecipientId == item.RecipientId).Seal is not null);
        return new(previews.Protect(JsonSerializer.Serialize(proof, StandaloneTriggersApi.Json)), expires, resource.Trigger.Revision, proof.CommandFingerprint,
            true, [new("Selected recipients", After: expected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("Dispatch commitments", After: sealedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("Effect", Description: kind == RuntimeIngressControlKind.CancelPendingRecipients
                    ? "Only selected unsealed new-root responsibility can be cancelled. Committed dispatch and ordinary waits remain owed."
                    : "Request recovery with the captured targets and inputs. This does not assert workflow execution started.")], []);
    }

    internal async ValueTask<TriggerChangeResult> ApplyAsync(string triggerId, JsonElement request, CancellationToken ct)
    {
        RequireControls();
        var command = request.GetProperty("command"); var kind = Kind(command); var requestId = RequestId(request);
        var resource = await RequireResourceAsync(triggerId, Permission(kind), ct);
        var (source, _) = await ResolveAsync(Text(command, "deliveryId"), triggerId, ct);
        var fingerprint = Fingerprint(new { Operation = "ApplyTriggerChange", TriggerId = triggerId, ExpectedRevision = Number(request, "expectedRevision"), Command = command });
        var replay = source.SubscriptionDelivery!.ControlFacts?.Receipts.SingleOrDefault(receipt => receipt.ActorId == access.Subject && receipt.RequestId == requestId);
        IReadOnlyList<RuntimeIngressRecipientExpectation> expected;
        if (replay is not null)
        {
            if (replay.RequestFingerprint != fingerprint || replay.Kind != kind) throw Changed();
            expected = [];
        }
        else
        {
            TriggerDeliveryPreviewProof proof;
            try { proof = JsonSerializer.Deserialize<TriggerDeliveryPreviewProof>(previews.Unprotect(Text(request, "previewId")), StandaloneTriggersApi.Json) ?? throw Changed(); }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException or FormatException) { throw Changed(); }
            expected = Select(source, triggerId, command);
            if (proof.Actor != access.Subject || proof.Scope != access.Scope || proof.TriggerId != triggerId ||
                proof.TriggerRevision != resource.Trigger.Revision || proof.TriggerRevision != Number(request, "expectedRevision") ||
                proof.SourceId != source.OutboxItemId || proof.SourceFingerprint != source.SourceFingerprint ||
                proof.SelectionFingerprint != source.SubscriptionDelivery.SelectionFingerprint || proof.CommandFingerprint != Fingerprint(command) ||
                proof.DeliveryRevision != Revision(source, triggerId) || proof.ExpiresAt <= time.GetUtcNow() ||
                Fingerprint(proof.Recipients) != Fingerprint(expected)) throw Changed();
        }
        var result = await ingress!.ControlAsync(new(access.Scope, source.OutboxItemId, source.SourceFingerprint,
            source.SubscriptionDelivery.SelectionFingerprint, triggerId, access.Subject, requestId, fingerprint, kind, expected)
            { ExpectedTriggerRevision = Number(request, "expectedRevision") }, ct);
        return new(requestId, result.Replayed ? "AlreadyApplied" : "Applied", Number(request, "expectedRevision"), null,
            Effects: result.Receipt.Recipients.Select(outcome => (object)new { recipientId = outcome.RecipientId, outcome = Outcome(kind, outcome.Outcome),
                message = "The source control receipt was committed; it does not assert workflow completion." }).ToArray());
    }

    private async ValueTask<TriggerDeliveryView> ViewAsync(RuntimePostCommitOutboxItem source, StandaloneTrigger trigger, CancellationToken ct)
    {
        var delivery = source.SubscriptionDelivery!;
        var recipients = new List<TriggerRecipientView>();
        var accepted = delivery.Recipients.Where(item => Owns(source, item.SubscriptionId, trigger.Id)).ToArray();
        var refused = delivery.Refusals.Where(item => Owns(source, item.SubscriptionId, trigger.Id)).ToArray();
        var retry = SupportsControls && await access.HasAsync(TriggerPermissions.Retry, trigger, ct);
        var cancel = SupportsControls && await access.HasAsync(TriggerPermissions.Cancel, trigger, ct);
        foreach (var recipient in accepted)
        {
            var progress = delivery.Progress.Single(item => item.RecipientId == recipient.RecipientId);
            var slotName = SlotName(recipient);
            var readable = slotName is not null && await access.HasAsync("workflows/runtime:read", new TriggerTargetResource(recipient.LogicalDefinitionId, slotName), ct);
            var findings = new List<TriggerFinding>();
            if (progress.LastFailureCode is { } failure) findings.Add(new(SafeCode(failure), "Recipient recovery requires attention."));
            TriggerExecutionReference? execution = null; string? currentVersion = null;
            if (readable)
            {
                var current = await targets.DescribeAsync(access.Scope, recipient.LogicalDefinitionId, slotName!, ct);
                currentVersion = current?.Version;
                execution = await ExecutionAsync(source, recipient, progress, ct);
            }
            else findings.Add(new("TargetDetailsRestricted", "Workflow details require ordinary workflow read permission."));
            var owed = progress.Phase is RuntimeRecipientPhase.Pending or RuntimeRecipientPhase.DispatchCommitted;
            var dispatch = execution is not null ? "Started" : progress.Phase switch
            {
                RuntimeRecipientPhase.Pending => "NotDispatched", RuntimeRecipientPhase.DispatchCommitted => "DispatchCommitted",
                RuntimeRecipientPhase.CancelledBeforeDispatch => "Cancelled", RuntimeRecipientPhase.TargetAccepted => progress.Receipt?.Kind switch
                { RuntimeTargetAcceptanceKind.InitialCheckpoint => progress.Receipt.EvidenceId == recipient.StartIdentity.WorkflowExecutionId ? "Started" : "Queued",
                    RuntimeTargetAcceptanceKind.SchedulerCommand or RuntimeTargetAcceptanceKind.ModelInput => "Queued", _ => "AdmissionUnknown" },
                _ => "AdmissionUnknown"
            };
            recipients.Add(new(recipient.RecipientId, RuntimeIngressInspection.RecipientRevision(progress), recipient.SubscriptionId,
                readable ? new(recipient.LogicalDefinitionId, slotName!) : null,
                readable ? recipient.SourceProvenance.SourceVersion ?? recipient.Material.ArtifactVersion : null, currentVersion,
                "Accepted", dispatch, readable ? recipient.StartIdentity.WorkflowExecutionId : null, execution, findings,
                [new("RetryRecipients", retry && owed, owed ? null : "NotEligible"),
                 new("CancelPendingRecipients", cancel && progress.Phase == RuntimeRecipientPhase.Pending,
                     progress.Phase == RuntimeRecipientPhase.Pending ? null : progress.Seal is not null ? "DispatchCommitted" : "NotEligible")], progress.RetryAt, progress.AttemptCount));
        }
        recipients.AddRange(refused.Select(item => new TriggerRecipientView("refused:" + item.SubscriptionId, item.SubscriptionRevision,
            item.SubscriptionId, null, null, null, "Refused", "NotDispatched", null, null,
            [new(SafeCode(item.ReasonCode), "This association was refused during source admission.")], [])));
        if (source.StandaloneIngress?.TriggerId == trigger.Id && delivery.WaitDelivery is { } waits)
            foreach (var wait in waits.Recipients) recipients.Add(await WaitViewAsync(wait, waits.Progress.Single(item => item.WaitId == wait.WaitId), ct));
        var capture = delivery.ResourceCaptures?.SingleOrDefault(item => item.TriggerId == trigger.Id);
        var transport = source.StandaloneIngress?.TriggerId == trigger.Id ? delivery.ControlFacts?.Transport.OrderBy(item => item.ObservedAt).LastOrDefault() : null;
        return new(EncodeId(source.OutboxItemId, trigger.Id), Revision(source, trigger.Id), trigger.Id, trigger.Name,
            capture?.TriggerRevision ?? source.StandaloneIngress?.TriggerRevision, source.StandaloneIngress?.SourceKey,
            refused.Length > 0 ? accepted.Length > 0 ? "PartiallyAccepted" : "Refused" : "Accepted",
            source.StandaloneIngress?.AdmissionPolicy.ToString(), accepted.Length, refused.Length, source.RecordedAt,
            source.StandaloneIngress?.RecordedAt,
            recipients, [], transport is null ? new("NotApplicable") : new(transport.State.ToString(), transport.StatusCode, transport.ObservedAt), trigger.Revision);
    }

    private async ValueTask<TriggerExecutionReference?> ExecutionAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeRecipientProgress progress, CancellationToken ct)
    {
        if (progress.Seal is null) return null;
        if (recipient.ModelProtocol is null)
        {
            var state = executions is null ? null : await executions.FindAsync(recipient.StartIdentity.WorkflowExecutionId, ct);
            if (state is null || !await access.HasAsync("workflows/runtime:read", state, ct)) return null;
            NativeIngressTargetEvidence.ValidateState(new(source, recipient, progress.Seal), state);
            return new(state.WorkflowExecutionId, "Workflow", state.Status.ToString(), time.GetUtcNow(),
                "/workflows/instances/" + Uri.EscapeDataString(state.WorkflowExecutionId));
        }
        var model = models is null ? null : await models.FindAsync(recipient.StartIdentity.WorkflowExecutionId, ct);
        if (model is null || model.Scope != access.Scope.Persistence || model.TenantId != access.Scope.TenantId ||
            model.Protocol != recipient.ModelProtocol || model.MaterialIncarnationId != recipient.MaterialIncarnation ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(model.Executable, recipient.Material) ||
            model.IngressOrigin?.IsEquivalentTo(NativeIngressTargetEvidence.Origin(source, recipient)) != true ||
            !await access.HasAsync("workflows/runtime:read", model, ct)) return null;
        return new(model.ExecutionId, recipient.ModelKey, model.Lifecycle.ToString(), time.GetUtcNow(),
            recipient.ModelKey == "bpmn" ? "/workflows/bpmn/runs/" + Uri.EscapeDataString(model.ExecutionId) : null);
    }

    private async ValueTask<TriggerRecipientView> WaitViewAsync(RuntimeIngressWaitRecipient wait, RuntimeIngressWaitProgress progress, CancellationToken ct)
    {
        var state = executions is null ? null : await executions.FindAsync(wait.Identity.WorkflowExecutionId, ct);
        var canRead = state is not null && wait.Expected.MatchesExecution(state, access.Scope.Persistence) &&
            await access.HasAsync("workflows/runtime:read", state, ct);
        var retired = progress.Receipt?.Kind == RuntimeIngressWaitAcceptanceKind.RetiredGeneration;
        return new(wait.WaitId, checked(1 + progress.AttemptCount * 3L + (long)progress.Phase), null, null,
            canRead ? wait.Expected.RegistrationCapture!.Executable.ArtifactVersion : null, null, "Accepted",
            progress.Phase switch { RuntimeIngressWaitPhase.Pending => "NotDispatched", RuntimeIngressWaitPhase.DispatchCommitted => "DispatchCommitted",
                _ => progress.Receipt?.Kind == RuntimeIngressWaitAcceptanceKind.SchedulerCommand ? "Queued" : retired ? "NotDispatched" : "Started" },
            null, canRead ? new(state!.WorkflowExecutionId, "Workflow", state.Status.ToString(), time.GetUtcNow(), "/workflows/instances/" + Uri.EscapeDataString(state.WorkflowExecutionId)) : null,
            [new(retired ? "RetiredOrdinaryWait" : "OrdinaryWait", "This saved ordinary wait retains its existing workflow execution and is read-only here.")],
            [new("RetryRecipients", false, "OrdinaryWait"), new("CancelPendingRecipients", false, "OrdinaryWait")], progress.RetryAt, progress.AttemptCount);
    }

    private static IReadOnlyList<RuntimeIngressRecipientExpectation> Select(RuntimePostCommitOutboxItem source, string triggerId, JsonElement command)
    {
        if (Number(command, "expectedDeliveryRevision") != Revision(source, triggerId)) throw Changed();
        var values = command.GetProperty("recipients").EnumerateArray().ToArray();
        if (values.Length is < 1 or > 500) throw Invalid("Choose between 1 and 500 recipients.");
        var result = new List<RuntimeIngressRecipientExpectation>();
        foreach (var value in values)
        {
            var id = Text(value, "recipientId");
            var recipient = source.SubscriptionDelivery!.Recipients.SingleOrDefault(item => item.RecipientId == id && Owns(source, item.SubscriptionId, triggerId));
            var progress = source.SubscriptionDelivery.Progress.SingleOrDefault(item => item.RecipientId == id);
            if (recipient is null || progress is null || RuntimeIngressInspection.RecipientRevision(progress) != Number(value, "expectedRevision")) throw Changed();
            result.Add(new(id, RuntimeIngressInspection.ProgressFingerprint(progress)));
        }
        if (result.Select(item => item.RecipientId).Distinct(StringComparer.Ordinal).Count() != result.Count) throw Invalid("Recipient identities must be distinct.");
        return result.OrderBy(item => item.RecipientId, StringComparer.Ordinal).ToArray();
    }
    private async ValueTask<(RuntimePostCommitOutboxItem Source, string TriggerId)> ResolveAsync(string id, string? expectedTrigger, CancellationToken ct)
    {
        string sourceId; string? owner;
        if (id.StartsWith("dv1.", StringComparison.Ordinal))
        {
            try { var parts = JsonSerializer.Deserialize<string[]>(Decode(id[4..])) ?? throw Missing();
                if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw Missing(); sourceId = parts[0]; owner = parts[1]; }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException) { throw Missing(); }
        }
        else { sourceId = id; owner = null; }
        var source = await ingress!.FindDeliveryAsync(access.Scope, sourceId, ct) ?? throw Missing();
        // The raw source identifier published by HTTP is only a locator; ordinary resource read authorization follows.
        owner ??= source.StandaloneIngress?.TriggerId;
        if (owner is null || expectedTrigger is not null && owner != expectedTrigger || !ResourceIds(source).Contains(owner, StringComparer.Ordinal)) throw Missing();
        return (source, owner);
    }
    private async ValueTask<TriggerAggregate> RequireResourceAsync(string id, string permission, CancellationToken ct)
    {
        var resource = await resources.FindAsync(access.Scope, id, ct) ?? throw Missing();
        await access.RequireAsync(permission, resource.Trigger, ct, mask: true);
        return resource; // Tombstones retain history authority independently of normal resource browsing.
    }
    internal static string EncodeId(string sourceId, string triggerId) => "dv1." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { sourceId, triggerId }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Decode(string value) { value = value.Replace('-', '+').Replace('_', '/'); return Encoding.UTF8.GetString(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='))); }
    private TriggerDeliveryCursor ReadCursor(string value, string? triggerId)
    {
        try
        {
            var cursor = JsonSerializer.Deserialize<TriggerDeliveryCursor>(cursors.Unprotect(value), StandaloneTriggersApi.Json) ?? throw Missing();
            if (cursor.Actor != access.Subject || cursor.Scope != access.Scope || cursor.TriggerId != triggerId) throw Invalid("The delivery cursor belongs to another query.");
            return cursor;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException) { throw Invalid("The delivery cursor is invalid."); }
    }
    private string ProtectCursor(TriggerDeliveryCursor value) => cursors.Protect(JsonSerializer.Serialize(value, StandaloneTriggersApi.Json));
    private static IEnumerable<string> ResourceIds(RuntimePostCommitOutboxItem source) => source.StandaloneIngress is { } external
        ? [external.TriggerId] : (source.SubscriptionDelivery?.ResourceCaptures?.Select(item => item.TriggerId) ?? []).Order(StringComparer.Ordinal);
    private static bool Owns(RuntimePostCommitOutboxItem source, string subscriptionId, string triggerId) => source.StandaloneIngress?.TriggerId == triggerId ||
        source.SubscriptionDelivery?.ResourceCaptures?.Any(item => item.TriggerId == triggerId && item.SubscriptionIds.Contains(subscriptionId, StringComparer.Ordinal)) == true;
    private static string? SlotName(RuntimeSubscriptionRecipient recipient)
    {
        var prefix = $"activation-slot:{recipient.LogicalDefinitionId.Length}:{recipient.LogicalDefinitionId}:";
        if (!recipient.SlotId.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var value = recipient.SlotId[prefix.Length..]; var separator = value.IndexOf(':');
        return separator > 0 && int.TryParse(value[..separator], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var length) && length > 0 && value[(separator + 1)..] is var slot && !string.IsNullOrWhiteSpace(slot) && slot.Length == length &&
            WorkflowActivationSlotIdentity.Create(recipient.LogicalDefinitionId, slot) == recipient.SlotId ? slot : null;
    }
    internal static long Revision(RuntimePostCommitOutboxItem source, string triggerId)
    {
        var delivery = source.SubscriptionDelivery!;
        var selected = delivery.Recipients.Where(item => Owns(source, item.SubscriptionId, triggerId)).Select(item => item.RecipientId).ToHashSet(StringComparer.Ordinal);
        return checked(1 + delivery.Progress.Where(item => selected.Contains(item.RecipientId)).Sum(RuntimeIngressInspection.RecipientRevision) +
            (delivery.ControlFacts?.Receipts.Count(item => item.Recipients.Any(result => selected.Contains(result.RecipientId))) ?? 0) +
            (source.StandaloneIngress?.TriggerId == triggerId ? (delivery.ControlFacts?.Transport.Count ?? 0) +
                (delivery.WaitDelivery?.Progress.Sum(item => 1 + item.AttemptCount * 3L + (long)item.Phase) ?? 0) : 0));
    }
    private static RuntimeIngressControlKind Kind(JsonElement command) => Text(command, "kind") switch
    { "RetryRecipients" => RuntimeIngressControlKind.RetryRecipients, "CancelPendingRecipients" => RuntimeIngressControlKind.CancelPendingRecipients, _ => throw Invalid("Choose a supported delivery control.") };
    private static string Permission(RuntimeIngressControlKind kind) => kind == RuntimeIngressControlKind.RetryRecipients ? TriggerPermissions.Retry : TriggerPermissions.Cancel;
    private static string Outcome(RuntimeIngressControlKind kind, RuntimeIngressControlOutcome outcome) => outcome switch
    { RuntimeIngressControlOutcome.RetryScheduled => "RetryRequested", RuntimeIngressControlOutcome.DeliveryInProgress => "AlreadyRequested",
        RuntimeIngressControlOutcome.Cancelled => "Cancelled", RuntimeIngressControlOutcome.DispatchCommitted => "DispatchCommitted",
        RuntimeIngressControlOutcome.AlreadyAccepted => kind == RuntimeIngressControlKind.RetryRecipients ? "AlreadyAdmitted" : "TargetAccepted", _ => "NotEligible" };
    private static string SafeCode(string value) => value.Length <= 100 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') ? value : "DeliveryUnavailable";
    private void RequireInspection() { if (!SupportsInspection) throw new TriggerOperationException("unavailable", "Physical delivery inspection is not available in this composition."); }
    private void RequireControls() { if (!SupportsControls) throw new TriggerOperationException("unavailable", "Protected delivery controls are not available in this composition."); }
    private static TriggerOperationException Missing() => new("missing", "Trigger delivery not found.");
    private static TriggerOperationException Changed() => new("conflict", "The reviewed resource or delivery state changed; reload it.");
    private static TriggerOperationException Invalid(string message) => new("validation", message);
}
