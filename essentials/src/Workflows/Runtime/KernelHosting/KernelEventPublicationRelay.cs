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
    KernelHostCapabilities capabilities, Func<IEventStartAdmissions> bind, TimeProvider clock, ILogger<KernelEventPublicationRelay> logger, bool retainsReceipts = false)
    : IKernelEventPublicationRegistration
{
    private const int RuntimePageSize = 16;
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(5);
    private (string Slot, long Revision)? _applyAfter;
    private (string Slot, long Revision)? _drainAfter;
    private string? _targetAfter;

    public bool SupportsDurableMode => retainsReceipts;

    public void Validate(KernelServingPlan plan, WorkflowActivationCommand command, WorkflowExecutableSourceReference reference,
        IReadOnlyList<WorkflowTriggerBinding> claims)
    {
        if (claims.Count == 0 || plan.Served is not { Workflow: { } workflow }) return;
        var source = new RuntimeKernelEventSource(command.ActivationId, reference.SourceReferenceId, reference.SourceKind, reference.SourceId,
            reference.SourceVersion, command.Executable.Identity)
        {
            Mode = claims[0].Metadata.ContainsKey(RuntimeKernelEventPublication.ModeMetadata) ? PersistenceMode.Durable : null
        };
        var value = new RuntimeKernelEventPublication(new(scope.Persistence, reference.TenantId), plan.SlotId, command.SlotName,
            command.Executable.Identity.DefinitionId, checked(command.ExpectedRevision + 1), source,
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

    public async ValueTask ApplyAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken)
    {
        using var budget = Limit(cancellationToken);
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        var services = runtime.ServiceProvider;
        var registrations = await BindAsync(publication, services, budget.Token);
        var applied = await bind().ApplyAsync(publication.SlotId, publication.Revision, registrations, budget.Token);
        if (applied is not (EventRegistrationResult.Applied or EventRegistrationResult.Duplicate) ||
            !await services.GetRequiredService<IRuntimeKernelEventPublicationStore>().AcknowledgeAsync(publication, budget.Token))
            throw new InvalidOperationException($"The exact Event registration revision was not acknowledged ({applied}).");
    }

    internal async Task RestoreAsync(CancellationToken token)
    {
        (string Slot, long Revision)? after = null;
        do
        {
            using var budget = Limit(token);
            var page = await PageAsync(false, after, budget.Token);
            after = Cursor(page);
            foreach (var publication in page) await ApplyAsync(publication, token);
        } while (after is not null);
    }

    internal async Task RunAsync(KernelExecutableProtection protection, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(Cadence, clock);
            do
            {
                await MaintainAsync(ApplyPageAsync, token);
                await MaintainAsync(RelayPageAsync, token);
                await MaintainAsync(DrainPageAsync, token);
                if (retainsReceipts) await MaintainAsync(PruneReceiptsAsync, token);
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }

        async Task PruneReceiptsAsync(CancellationToken operation)
        {
            var provider = bind();
            while (await provider.PruneSettledAsync(operation) != 0) operation.ThrowIfCancellationRequested();
        }

        async Task ApplyPageAsync(CancellationToken operation)
        {
            var page = await PageAsync(false, _applyAfter, operation);
            _applyAfter = Cursor(page);
            foreach (var item in page)
                await MaintainAsync(ct => ApplyAsync(item, ct).AsTask(), operation);
        }
        async Task RelayPageAsync(CancellationToken operation)
        {
            var provider = bind();
            var targets = await provider.ListAsync(_targetAfter, operation);
            _targetAfter = targets.Count == 0 ? null : targets[^1].Id;
            foreach (var target in targets)
                await MaintainAsync(ct => AdmitAsync(provider, target, protection, ct), operation);
        }
        async Task DrainPageAsync(CancellationToken operation)
        {
            var page = await PageAsync(true, _drainAfter, operation);
            _drainAfter = Cursor(page);
            var provider = bind();
            foreach (var item in page)
                await MaintainAsync(async ct =>
                {
                    if (!await provider.ConfirmDrainedAsync(item.SlotId, item.Revision, ct)) return;
                    await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
                    await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>().ReleaseDrainedAsync(item, ct);
                }, operation);
        }
    }

    private async Task<IReadOnlyList<RuntimeKernelEventPublication>> PageAsync(bool obsolete, (string Slot, long Revision)? after,
        CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
        return await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
            .ListAsync(scope, obsolete, after?.Slot, after?.Revision ?? 0, RuntimePageSize, token);
    }

    private static (string Slot, long Revision)? Cursor(IReadOnlyList<RuntimeKernelEventPublication> page) =>
        page.Count < RuntimePageSize ? null : (page[^1].SlotId, page[^1].Revision);

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
