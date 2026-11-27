using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Qualifies a private source-owned start without registering a live standalone target.</summary>
internal sealed class KernelStandalonePreparedStartHandoff(IServiceScopeFactory scopes, PersistenceScope persistence,
    BoundScope actors, KernelHostCapabilities capabilities, Func<IPreparedRootStartHandoff> foundation) : IRuntimePreparedStandaloneRecipient
{
    public bool Selects(RuntimeStandaloneIngressSource source, RuntimeSubscriptionRecipient recipient) =>
        source.Scope == persistence && (source.TenantId ?? PersistenceScope.DefaultValue) == actors.Tenant &&
        recipient.ModelKey == "native" && recipient.ModelProtocol is null &&
        source.CanonicalPayload.TryGetProperty("trigger", out var trigger) && trigger.ValueKind == JsonValueKind.Object &&
        trigger.TryGetProperty("typeKey", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is "event" or "timer" or "cron";

    public async ValueTask<RuntimePostCommitOutboxItem> DeliverAsync(RuntimeDeliverySourceContext source, string recipientId,
        CancellationToken token)
    {
        if (source.Scope != persistence || persistence.Value != actors.Tenant)
            throw new InvalidOperationException("The standalone source is outside this Foundation owner's scope.");
        await using var runtime = await scopes.CreateAsyncScopeAsync(persistence);
        var services = runtime.ServiceProvider;
        var handoff = RequireHandoff(services);
        var committed = await handoff.CommitPreparedStartAsync(source, recipientId, token);
        if (committed is null)
            return await services.GetRequiredService<IRuntimeIngressDeliveryStore>().ReadSourceAsync(source, token);
        var recipient = committed.Recipient;
        if ((recipient.TenantId ?? PersistenceScope.DefaultValue) != actors.Tenant)
            throw new InvalidOperationException("The standalone recipient is outside this Foundation owner's tenant.");
        var seal = committed.Seal;
        var authority = new RuntimeCommittedRecipientAuthority(seal.SourceOutboxItemId, seal.SourceFingerprint,
            seal.SelectionFingerprint, persistence, recipientId, seal.Fingerprint);
        var item = committed.Item;
        if (Progress(item).Receipt is not null) return item;

        var attempt = await handoff.ReadPreparedStartAttemptAsync(authority, token);
        if (attempt is { Disarmed: false } pending)
        {
            item = await handoff.ReconcilePreparedStartAsync(source, authority, pending.Generation, token);
            if (Progress(item).Receipt is not null) return item;
            attempt = await handoff.ReadPreparedStartAttemptAsync(authority, token);
            if (attempt is not { Disarmed: true }) return item;
        }

        var material = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(recipient.Material.ArtifactId, token);
        if (material is null || material.Identity != recipient.Material || material.InputContract is not { } contract ||
            RuntimeIngressAdmission.ComputeInputContractFingerprint(contract) != recipient.InputContractFingerprint)
            throw new InvalidDataException("The exact historical standalone executable or its declared input contract is unavailable.");
        var validated = services.GetRequiredService<IWorkflowExecutableInputValidator>().Validate(contract, recipient.Inputs);
        if (!validated.IsValid ||
            !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(validated.NormalizedInputs), JsonSerializer.SerializeToElement(recipient.Inputs)))
            throw new InvalidDataException("The standalone recipient's input capture no longer validates exactly.");
        var policy = services.GetRequiredService<IWorkflowExecutableStartPolicy>();
        var allowed = await policy.EvaluateAsync(new(recipient.Material, WorkflowExecutableStartAuthorityKind.StandaloneTrigger,
            recipient.Authority.SystemIdentity, recipient.Authority, WorkflowRunKind.PublishedRun, recipient.TenantId, recipient.Partition), token);
        if (!allowed.IsAllowed)
            throw new TriggerOperationException("target-start-policy-denied", "The current host policy blocks this accepted recipient.");
        var mode = KernelPersistenceMode.Resolve(services, material);
        if (mode == PersistenceMode.Ephemeral)
            throw new NotSupportedException("Prepared standalone qualification requires a durable actor.");
        var input = new JsonObject();
        foreach (var (name, value) in validated.NormalizedInputs) input.Add(name, JsonNode.Parse(value.GetRawText()));
        var workflow = NativeExecutableMapper.Map(material, capabilities, recipient.LogicalDefinitionId);
        var start = WorkflowActor.Start(NativeExecutionModel.Start(workflow, mode.ToString(), input));
        var previous = attempt?.Generation ?? 0L;
        var operation = RuntimeDeliveryFingerprint.Compute("elsa.kernel.standalone-prepared-attempt.v1",
            new { sealFingerprint = seal.Fingerprint, generation = checked(previous + 1) });
        _ = await foundation().StartAsync(operation, start, ProtectAsync, token);
        var recorded = await handoff.ReadPreparedStartAttemptAsync(authority, token);
        return recorded is { } receipt
            ? await handoff.ReconcilePreparedStartAsync(source, authority, receipt.Generation, token)
            : await services.GetRequiredService<IRuntimeIngressDeliveryStore>().ReadSourceAsync(source, token);

        RuntimeRecipientProgress Progress(RuntimePostCommitOutboxItem current) =>
            current.SubscriptionDelivery!.Progress.Single(progress => progress.RecipientId == recipientId);

        async ValueTask<bool> ProtectAsync(PendingRootStart offer, CancellationToken protectionToken)
        {
            if (offer.Tenant != actors.Tenant || offer.Type != start.Type || offer.TypeData != start.TypeData)
                return false;
            await using var pinScope = await scopes.CreateAsyncScopeAsync(persistence);
            var pinServices = pinScope.ServiceProvider;
            var generation = await RequireHandoff(pinServices).TryBindPreparedStartAsync(source, authority, previous,
                offer.Tenant, offer.Actor.Id, offer.Issuance, protectionToken);
            if (generation is null) return false;
            return await pinServices.GetRequiredService<IRuntimeKernelExecutablePinStore>().TryPinAsync(
                [new(persistence, actors.Tenant, offer.Actor.Id, recipient.Material, offer.Issuance)], protectionToken);
        }
    }

    private static IRuntimePreparedStartHandoff RequireHandoff(IServiceProvider services) =>
        services.GetRequiredService<IRuntimeIngressDeliveryStore>() as IRuntimePreparedStartHandoff ??
        throw new NotSupportedException("Prepared standalone qualification requires the selected Runtime EF handoff.");
}

internal sealed class KernelStandalonePreparedStartProof(PersistenceScope persistence, BoundScope actors,
    Func<IPreparedRootStartHandoff> foundation) : IRuntimePreparedStartAttemptProof
{
    public async ValueTask<RuntimePreparedStartProof> ObserveAsync(TriggerScope scope, string actorReference, string issuance,
        CancellationToken cancellationToken)
    {
        if (scope.Persistence != persistence || (scope.TenantId ?? PersistenceScope.DefaultValue) != actors.Tenant)
            return RuntimePreparedStartProof.Unresolved;
        return (await foundation().ObserveAsync(new(actorReference), issuance, cancellationToken)) switch
        {
            PreparedRootStartDisposition.Accepted => RuntimePreparedStartProof.Accepted,
            PreparedRootStartDisposition.Disarmed => RuntimePreparedStartProof.Disarmed,
            _ => RuntimePreparedStartProof.Unresolved
        };
    }
}
