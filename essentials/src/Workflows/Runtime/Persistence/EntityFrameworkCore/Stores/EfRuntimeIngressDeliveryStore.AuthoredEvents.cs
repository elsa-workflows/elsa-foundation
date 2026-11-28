using System.Data;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    internal async ValueTask<bool> AdmitAuthoredEventAsync(RuntimeAuthoredStandaloneEvent source,
        Func<CancellationToken, ValueTask<IReadOnlyList<TriggerAggregate>>> readMatching,
        Func<TriggerAggregate, CancellationToken, ValueTask<RuntimeIngressAdmissionRequest>> prepare,
        CancellationToken token)
    {
        Require(source.Scope, token);
        RequireOwnedBoundary();
        if (!PreparedStandaloneOnly)
            throw new NotSupportedException("Authored actor Event ingress is owned by prepared standalone Runtime persistence.");
        var id = EfRelationalIdentity.HashLengthFramed("authored-event-cohort/1", source.Scope.Persistence.Value,
            source.Scope.TenantId!, source.Occurrence);
        var fingerprint = RuntimeDeliveryFingerprint.Compute("elsa.runtime.authored-event-cohort.v1", new
        {
            scope = source.Scope.Persistence.Value, tenant = source.Scope.TenantId, partition = source.Partition.Value,
            source.Occurrence, source.PublisherActor, source.EventName, payload = source.Payload.GetRawText(),
            committedAt = source.CommittedAt.ToUnixTimeMilliseconds(), source.Authority
        });
        if (await HasReceiptAsync(token)) return true;

        try
        {
            var observed = await readMatching(token);
            var candidates = new List<RuntimeIngressAdmissionRequest>(observed.Count);
            foreach (var resource in observed)
            {
                var request = await prepare(resource, token);
                RuntimeIngressAdmissionValidation.CheckCandidate(request, allowEmpty: true);
                RuntimeIngressAdmissionValidation.CheckAggregate(request, resource);
                PreparedStandaloneAdmission.RequireSource(request.Candidate);
                var ingress = request.Candidate.StandaloneIngress!;
                if (request.Scope != source.Scope || ingress.SourceKey != source.Occurrence ||
                    ingress.RecordedAt != source.CommittedAt || ingress.SourceFamily != EfAuthoredStandaloneEventIngress.BroadcastFamily ||
                    request.Candidate.SubscriptionDelivery!.Recipients.Any(recipient => !preparedTarget!.Selects(ingress, recipient)))
                    throw new InvalidDataException("A prepared resource does not belong to the committed authored Event.");
                candidates.Add(request);
            }
            var recipients = candidates.SelectMany(request => request.Candidate.SubscriptionDelivery!.Recipients).ToArray();
            var material = (EfWorkflowExecutableStore)executables.GetMaterialStore();
            if (recipients.Length == 0)
                await CommitAsync([], token);
            else
                await leases.ExecuteFencedAsync(recipients.Select(recipient => recipient.Material).Distinct().ToArray(),
                    "authored-event:" + id + ":" + Guid.NewGuid().ToString("N"), CommitAsync, token);
            return true;

            async ValueTask CommitAsync(IReadOnlyList<WorkflowExecutableRootWriteLease> grants, CancellationToken ct)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                if (await HasReceiptAsync(ct)) return;
                IReadOnlyList<(WorkflowExecutableRootWriteLease Grant, DateTimeOffset ExpiresAt)> deadlines = grants.Count == 0
                    ? [] : await material.FenceRootWriteLeasesAsync(grants, time, ct);
                // The indexed type/enabled discovery also fences additions and activations in the matching range.
                var current = await readMatching(ct);
                if (!current.Select(Generation).SequenceEqual(observed.Select(Generation)))
                    throw new TriggerOperationException("conflict", "The matching Event resources changed before cohort acceptance.");
                for (var index = 0; index < current.Count; index++)
                {
                    var aggregate = current[index];
                    var request = candidates[index];
                    if (!await EfStandaloneTriggerStore.LockResourceAsync(context, source.Scope, aggregate.Trigger.Id,
                            request.ExpectedTriggerRevision, ct))
                        throw new TriggerOperationException("conflict", "An Event resource changed before cohort acceptance.");
                    RuntimeIngressAdmissionValidation.CheckAggregate(request, aggregate);
                }
                foreach (var pair in candidates.SelectMany(request => request.Targets.Select(target => (request, target)))
                             .OrderBy(pair => pair.target.DefinitionId, StringComparer.Ordinal).ThenBy(pair => pair.target.SlotName, StringComparer.Ordinal))
                    await FenceTargetAsync(pair.request, pair.target, ct);
                foreach (var recipient in recipients.OrderBy(recipient => recipient.StartIdentity.WorkflowExecutionId, StringComparer.Ordinal))
                    await FenceRecipientAsync(source.Scope.Persistence, recipient, ct);
                foreach (var request in candidates.Where(request => request.Candidate.SubscriptionDelivery!.Recipients.Count > 0))
                    StageSource(request);
                context.AuthoredEventCohorts.Add(new()
                {
                    Id = id, SourceFingerprint = fingerprint,
                    ContentJson = JsonSerializer.Serialize(new AuthoredEventReceipt(1, source.Occurrence, source.PublisherActor,
                        candidates.Select(request => new AuthoredEventResource(request.Candidate.StandaloneIngress!.TriggerId,
                            request.ExpectedTriggerRevision, request.Candidate.StandaloneIngress.AssociationGeneration,
                            request.Candidate.SubscriptionDelivery!.SelectionFingerprint)).ToArray()))
                });
                EnsureLive();
                await context.SaveChangesAsync(ct);
                EnsureLive();
                await transaction.CommitAsync(ct);

                void EnsureLive()
                {
                    ct.ThrowIfCancellationRequested();
                    var now = time.GetUtcNow();
                    if (source.CommittedAt > now || candidates.Any(request => request.Targets.Any(target =>
                        request.Candidate.SubscriptionDelivery!.Recipients.Any(recipient => recipient.SubscriptionId == target.SubscriptionId) &&
                        target.SourceReference?.IsLive(now) != true)))
                        throw new TriggerOperationException("conflict", "An authored Event source or publication is outside its admission lifetime.");
                    foreach (var deadline in deadlines.Where(deadline => now >= deadline.ExpiresAt))
                        throw new Core.Exceptions.WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            // An identical concurrent winner or an acknowledgement loss is safe to replay without rediscovery.
            context.ChangeTracker.Clear();
            if (await HasReceiptAsync(token)) return true;
            throw;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }

        async ValueTask<bool> HasReceiptAsync(CancellationToken ct)
        {
            var found = await context.AuthoredEventCohorts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
            if (found is null) return false;
            if (found.SourceFingerprint != fingerprint)
                throw new TriggerOperationException("conflict", "The authored Event identity already names different committed content.");
            var receipt = JsonSerializer.Deserialize<AuthoredEventReceipt>(found.ContentJson);
            if (receipt is not { Version: 1, Resources: not null } || receipt.Occurrence != source.Occurrence ||
                receipt.PublisherActor != source.PublisherActor || receipt.Resources.Any(resource => resource is null ||
                    string.IsNullOrWhiteSpace(resource.Id) || resource.Revision <= 0 || resource.AssociationGeneration <= 0 ||
                    resource.SelectionFingerprint is not { Length: 64 }) ||
                receipt.Resources.Select(resource => resource.Id).Distinct(StringComparer.Ordinal).Count() != receipt.Resources.Length)
                throw new InvalidDataException("The persisted authored Event receipt has an invalid identity or cohort.");
            return true;
        }

        static (string Id, long Revision, long Configuration, long Association) Generation(TriggerAggregate aggregate) =>
            (aggregate.Trigger.Id, aggregate.Trigger.Revision, aggregate.Trigger.ConfigurationRevision, aggregate.Trigger.AssociationRevision);
    }

    private sealed record AuthoredEventReceipt(int Version, string Occurrence, string PublisherActor, AuthoredEventResource[] Resources);
    private sealed record AuthoredEventResource(string Id, long Revision, long AssociationGeneration, string SelectionFingerprint);
}
