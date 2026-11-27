using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Reconciles committed Runtime publication generations and their provider-selected starts.</summary>
internal sealed class KernelEventPublicationRelay(IServiceScopeFactory scopes, TriggerScope scope, BoundScope actors,
    KernelHostCapabilities capabilities, Func<IEventStartAdmissions> bind, TimeProvider clock, ILogger<KernelEventPublicationRelay> logger,
    bool retainsReceipts = false, bool orderedScan = false, bool adoptExisting = false)
    : IKernelEventPublicationRegistration
{
    private const int RuntimePageSize = 16;
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(5);
    private readonly Scan _apply = new();
    private readonly Scan _drain = new();
    private readonly Scan _targets = new();
    private readonly LegacyScan _legacyApply = new();
    private readonly LegacyScan _legacyDrain = new();
    private string? _targetAfter;

    private sealed class Scan
    {
        internal long? Through { get; set; }
        internal long After { get; set; }

        internal void Reset() => (Through, After) = (null, 0);
    }

    private sealed class LegacyScan
    {
        internal (string Slot, long Revision)? After { get; set; }
    }

    public bool SupportsDurableMode => retainsReceipts;
    public bool AdoptsExistingPublications => adoptExisting;

    public void Validate(KernelServingPlan plan, WorkflowExecutableIdentity identity, WorkflowExecutableSourceReference reference, long revision,
        IReadOnlyList<WorkflowTriggerBinding> claims)
    {
        if (claims.Count == 0 || plan.Served is not { Workflow: { } workflow } served) return;
        var source = new RuntimeKernelEventSource(served.ActivationId, reference.SourceReferenceId, reference.SourceKind, reference.SourceId,
            reference.SourceVersion, identity)
        {
            Mode = claims[0].Metadata.ContainsKey(RuntimeKernelEventPublication.ModeMetadata) ? PersistenceMode.Durable : null
        };
        var value = new RuntimeKernelEventPublication(new(scope.Persistence, reference.TenantId), plan.SlotId, served.SlotName,
            identity.DefinitionId, revision, source,
            claims.Select(claim => new RuntimeKernelEventEntry(claim.ExecutableNodeId,
                claim.Metadata[RuntimeKernelEventPublication.EventNameMetadata])).ToArray());
        KernelScheduleAuthority.RequireBoundScope(value.Scope, actors);
        EventStartRegistration.ValidatePublication(value.SlotId, Compose(value, source, workflow));
    }

    public async ValueTask<RuntimeKernelEventPublication?> ReadAsync(WorkflowActivationSlot slot, CancellationToken cancellationToken)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        return await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .FindAsync(scope, slot.SlotId, slot.Revision, cancellationToken);
    }

    public ValueTask ApplyAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken) =>
        ApplyAsync(publication, restore: false, cancellationToken);

    private async ValueTask ApplyAsync(RuntimeKernelEventPublication publication, bool restore, CancellationToken cancellationToken)
    {
        using var budget = Limit(cancellationToken);
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        var services = runtime.ServiceProvider;
        var store = services.GetRequiredService<IRuntimeKernelEventPublicationStore>();
        while (true)
        {
            EventStartRegistration[] registrations;
            try { registrations = await BindAsync(publication, services, budget.Token); }
            catch (InvalidDataException) when (restore)
            {
                // A successor can drain the old publication and release its material after the scan read it.
                if (await ReadSuccessorAsync() is not { } successor) throw;
                publication = successor;
                continue;
            }
            var applied = await bind().ApplyAsync(publication.SlotId, publication.Revision, registrations, budget.Token);
            if (applied is (EventRegistrationResult.Applied or EventRegistrationResult.Duplicate) &&
                await store.AcknowledgeAsync(publication, budget.Token))
                return;

            // A stale provider response alone is insufficient. A durable Runtime successor also explains
            // why an otherwise successful provider write could not acknowledge this exact generation.
            if (restore && await ReadSuccessorAsync() is { } next)
            {
                publication = next;
                continue;
            }
            throw new InvalidOperationException($"The exact Event registration revision was not acknowledged ({applied}).");
        }

        async ValueTask<RuntimeKernelEventPublication?> ReadSuccessorAsync()
        {
            var current = await store.FindCurrentAsync(publication.Scope, publication.SlotId, budget.Token);
            return current?.Revision > publication.Revision ? current : null;
        }
    }

    internal async Task RestoreAsync(CancellationToken token)
    {
        (string Slot, long Revision)? legacyAfter = null;
        while (true)
        {
            IReadOnlyList<RuntimeKernelEventPublication> legacy;
            using (var read = Limit(token)) legacy = await ReadLegacyPageAsync(false, legacyAfter, read.Token);
            if (legacy.Count == 0) break;
            foreach (var publication in legacy) await ApplyAsync(publication, restore: true, token);
            legacyAfter = (legacy[^1].SlotId, legacy[^1].Revision);
        }

        long through;
        using (var read = Limit(token)) through = await ReadRuntimeTailAsync(read.Token);
        long after = 0;
        while (after < through)
        {
            IReadOnlyList<RuntimeKernelEventPublicationScanItem> page;
            using (var read = Limit(token)) page = await ReadRuntimePageAsync(false, after, through, read.Token);
            if (page.Count == 0) return;
            foreach (var item in page) await ApplyAsync(item.Publication, restore: true, token);
            after = page[^1].Ordinal;
        }
    }

    internal async Task RunAsync(KernelExecutableProtection protection, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(Cadence, clock);
            do
            {
                await VisitLegacyPageAsync(_legacyApply, false, (item, ct) => ApplyAsync(item, ct).AsTask(), token);
                await VisitOrderedPageAsync(_apply, ReadRuntimeTailAsync,
                    (after, through, ct) => ReadRuntimePageAsync(false, after, through, ct),
                    item => item.Ordinal, (item, ct) => ApplyAsync(item.Publication, ct).AsTask(), token);
                await RelayPageAsync(token);
                await VisitLegacyPageAsync(_legacyDrain, true, ReleaseDrainedAsync, token);
                await VisitOrderedPageAsync(_drain, ReadRuntimeTailAsync,
                    (after, through, ct) => ReadRuntimePageAsync(true, after, through, ct),
                    item => item.Ordinal, (item, ct) => ReleaseDrainedAsync(item.Publication, ct), token);
                if (retainsReceipts) await MaintainAsync(async ct => { await bind().PruneSettledAsync(ct); }, token);
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }

        async Task RelayPageAsync(CancellationToken stopping)
        {
            IEventStartAdmissions? provider = null;
            await MaintainAsync(_ =>
            {
                provider = bind();
                return Task.CompletedTask;
            }, stopping);
            if (provider is null) return;
            if (orderedScan)
            {
                var scan = (IOrderedEventStartAdmissions)provider;
                await VisitOrderedPageAsync(_targets, scan.ReadPendingTailAsync,
                    (after, through, ct) => scan.ReadPendingPageAsync(after, through, RuntimePageSize, ct),
                    item => item.Ordinal, (item, ct) => AdmitAsync(provider, item.Target, protection, ct), stopping);
                return;
            }

            IReadOnlyList<EventStartTarget>? targets = null;
            await MaintainAsync(async ct => targets = await provider.ListAsync(_targetAfter, ct), stopping);
            if (targets is null) return;
            if (targets.Count == 0) _targetAfter = null;
            await VisitPageAsync(targets, (target, ct) => AdmitAsync(provider, target, protection, ct),
                target => _targetAfter = target.Id, stopping);
        }
    }

    private async Task<long> ReadRuntimeTailAsync(CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        return await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .ReadScanTailAsync(scope, token);
    }

    private async Task<IReadOnlyList<RuntimeKernelEventPublicationScanItem>> ReadRuntimePageAsync(bool obsolete,
        long after, long through, CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        return await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .ListScanAsync(scope, obsolete, after, through, RuntimePageSize, token);
    }

    private async Task<IReadOnlyList<RuntimeKernelEventPublication>> ReadLegacyPageAsync(bool obsolete,
        (string Slot, long Revision)? after, CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        return await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .ListLegacyScanAsync(scope, obsolete, after?.Slot, after?.Revision ?? 0, RuntimePageSize, token);
    }

    private async Task ReleaseDrainedAsync(RuntimeKernelEventPublication publication, CancellationToken token)
    {
        if (!await bind().ConfirmDrainedAsync(publication.SlotId, publication.Revision, token)) return;
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>().ReleaseDrainedAsync(publication, token);
    }

    private async Task VisitOrderedPageAsync<T>(Scan scan, Func<CancellationToken, Task<long>> readTail,
        Func<long, long, CancellationToken, Task<IReadOnlyList<T>>> readPage, Func<T, long> ordinal,
        Func<T, CancellationToken, Task> visit, CancellationToken token)
    {
        IReadOnlyList<T>? page = null;
        await MaintainAsync(async ct =>
        {
            scan.Through ??= await readTail(ct);
            page = scan.Through == 0 ? [] : await readPage(scan.After, scan.Through.Value, ct);
        }, token);
        if (page is null) return;
        await VisitPageAsync(page, visit, item =>
        {
            var next = ordinal(item);
            if (next <= scan.After || next > scan.Through)
                throw new InvalidDataException("An Event scan did not advance within its committed boundary.");
            scan.After = next;
        }, token);

        if (page.Count == 0 || scan.After == scan.Through) scan.Reset();
    }

    private async Task VisitLegacyPageAsync(LegacyScan scan, bool obsolete,
        Func<RuntimeKernelEventPublication, CancellationToken, Task> visit, CancellationToken token)
    {
        IReadOnlyList<RuntimeKernelEventPublication>? page = null;
        await MaintainAsync(async ct => page = await ReadLegacyPageAsync(obsolete, scan.After, ct), token);
        if (page is null) return;
        if (page.Count == 0) scan.After = null;
        await VisitPageAsync(page, visit, item => scan.After = (item.SlotId, item.Revision), token);
    }

    private async Task VisitPageAsync<T>(IReadOnlyList<T> page, Func<T, CancellationToken, Task> visit,
        Action<T> advance, CancellationToken token)
    {
        // Yield between attempts. Each item has its own deadline, so a timeout leaves the unvisited suffix in the scan.
        var started = clock.GetTimestamp();
        foreach (var item in page)
        {
            token.ThrowIfCancellationRequested();
            await MaintainAsync(ct => visit(item, ct), token);
            advance(item);
            if (clock.GetElapsedTime(started) >= ReadBudget) break;
        }
    }

    private async Task AdmitAsync(IEventStartAdmissions provider, EventStartTarget target,
        KernelExecutableProtection protection, CancellationToken token)
    {
        if (target.Tenant != actors.Tenant)
            throw new InvalidDataException("The accepted Event target is outside the bound tenant.");
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        var services = runtime.ServiceProvider;
        var publication = await services.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .FindAsync(scope, target.Slot, target.Revision, token);
        // A concurrent successful settlement may let the preceding registration root be released.
        if (publication is null) return;
        var start = (await BindAsync(publication, services, token)).SingleOrDefault(item => item.Node == target.Registration.Node);
        if (start != target.Registration)
            throw new InvalidDataException("The selected Event target does not match its retained publication.");
        var admission = await provider.AdmitAsync(target.Id, selected =>
        {
            if (selected != target) throw new InvalidDataException("The accepted Event target changed before admission.");
            return start.Template with { Input = StartInput(target) };
        }, (prepared, ct) => protection.ProtectEventStartAsync(prepared, target, ct), token);
        if (admission.Outcome is not (AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate))
            logger.LogWarning("Event target {Target} in tenant {Tenant} awaits admission ({Reason}).",
                target.Id, actors.Tenant, admission.Reason);
    }

    private async ValueTask<EventStartRegistration[]> BindAsync(RuntimeKernelEventPublication value, IServiceProvider services, CancellationToken token)
    {
        KernelScheduleAuthority.RequireBoundScope(value.Scope, actors);
        if (RuntimeKernelScheduleControl.CanonicalScope(value.Scope) != RuntimeKernelScheduleControl.CanonicalScope(scope) ||
            value.SlotId != WorkflowActivationSlotIdentity.Create(value.DefinitionId, value.SlotName))
            throw new InvalidDataException("The Event publication is outside its bound slot or tenant.");
        if (value.Source is not { } source) return [];
        if (source.Executable.DefinitionId != value.DefinitionId ||
            source.ReferenceId != WorkflowActivationReferenceIdentity.Create(source.ActivationId))
            throw new InvalidDataException("The Event registration has inconsistent publication provenance.");

        var material = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(source.Executable.ArtifactId, token);
        if (material is null || material.Identity.ArtifactId != source.Executable.ArtifactId ||
            material.Identity.ArtifactHash != source.Executable.ArtifactHash)
            throw new InvalidDataException("The immutable Event executable is unavailable.");
        var workflow = NativeExecutableMapper.Map(material, capabilities, value.DefinitionId);
        return Compose(value, source, workflow);
    }

    private static EventStartRegistration[] Compose(RuntimeKernelEventPublication value, RuntimeKernelEventSource source, Workflow workflow)
    {
        if (workflow.ExecutableSelection is not { } selection)
            throw new InvalidDataException("The immutable Event executable selection is unavailable.");
        var nodes = NativeStartTriggers.StartEvents(workflow).ToDictionary(node => node.Id, StringComparer.Ordinal);
        if (nodes.Count != value.Entries.Count)
            throw new InvalidDataException("The immutable Event executable does not match the registered start nodes.");

        var published = new WorkflowPublishedStart(source.ReferenceId, source.Kind, source.SourceId, source.SourceVersion,
            source.Executable.DefinitionVersionId, source.Executable.ArtifactVersion, source.ActivationId, value.SlotId,
            value.SlotName, value.Revision, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source))), null);
        var template = WorkflowActor.Start(NativeExecutionModel.Start(workflow), published) with { Input = null };
        return value.Entries.Select(entry =>
        {
            if (!nodes.TryGetValue(entry.Node, out var node) || KernelEventClaims.Name(node) != entry.Name)
                throw new InvalidDataException("The immutable Event executable does not match the registered Event name.");
            return new EventStartRegistration(entry.Node, "event:" + entry.Name, source.ActivationId, source.ReferenceId,
                workflow.Id, selection, template)
            {
                Mode = source.Mode switch
                {
                    null or PersistenceMode.Balanced => null,
                    PersistenceMode.Durable => nameof(PersistenceMode.Durable),
                    _ => throw new InvalidDataException("The selected Event publication does not have a durable persistence mode.")
                }
            };
        }).ToArray();
    }

    private static string StartInput(EventStartTarget target)
    {
        if (target.Payload is { } payload)
        {
            // Validate the provider's JSON and keep its raw bytes when embedding it in the Native start.
            using var json = JsonDocument.Parse(payload);
        }
        return "{\"Mode\":" + JsonSerializer.Serialize(target.Registration.Mode ?? Modes.Balanced) +
            ",\"Input\":null,\"TriggerNode\":" + JsonSerializer.Serialize(target.Registration.Node) +
            ",\"Trigger\":" + (target.Payload ?? "null") + "}";
    }

    private async Task MaintainAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            using var budget = Limit(token);
            await action(budget.Token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Event registration or delivery will retry in tenant {Tenant}.", actors.Tenant);
        }
    }

    private static CancellationTokenSource Limit(CancellationToken token)
    {
        var result = CancellationTokenSource.CreateLinkedTokenSource(token);
        result.CancelAfter(ReadBudget);
        return result;
    }
}
