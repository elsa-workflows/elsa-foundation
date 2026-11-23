using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;

namespace IndependentTriggers.Consumer;

/// <summary>A separately compiled source/type module using the public Core boundary only.</summary>
public static class ConsumerModule
{
    public const string TypeKey = "independent-consumer";
    public static ValueTypeDescriptor Schema { get; } = new("Independent.Payload", schemaVersion: 1);
    public static StandaloneTriggerSourceDescriptor Descriptor { get; } = new(TypeKey, 1, "Independent.Consumer", Schema);

    public static void AddSource(IServiceCollection services, StandaloneTriggerInvocationContext trustedContext)
    {
        services.AddSingleton(trustedContext);
        services.AddSingleton<ConsumerAuthorization>();
        services.AddScoped<IStandaloneTriggerSourceContext<ConsumerSource>, ConsumerContext>();
        services.AddStandaloneTriggerIngress<ConsumerSource>(Descriptor);
    }

    public static void AddAuthoring(IServiceCollection services)
    {
        services.AddSingleton<IStandaloneTriggerTypeSource, ConsumerTypes>();
        services.AddScoped<ITriggerConfigurationValidator, ConsumerValidator>();
    }

    public static void AddOtherSource(IServiceCollection services, bool conflict = false)
    {
        services.AddScoped<IStandaloneTriggerSourceContext<OtherConsumerSource>, OtherConsumerContext>();
        services.AddStandaloneTriggerIngress<OtherConsumerSource>(Descriptor with { SourceFamily = conflict ? Descriptor.SourceFamily : "Independent.Other" });
    }

    /// <summary>Host supplies its selected Core adapter; the extension never resolves an implementation type.</summary>
    public static void AddTargetDecorator(IServiceCollection services,
        Func<IServiceProvider, IStandaloneTriggerRuntimeTarget> selectedTarget)
    {
        services.AddSingleton<ConsumerProbe>();
        services.AddScoped<IStandaloneTriggerRuntimeTarget>(provider => new ConsumerTarget(selectedTarget(provider), provider.GetRequiredService<ConsumerProbe>()));
    }

    public static ValueTask<StandaloneTriggerIngressResult> SubmitAsync(IServiceProvider provider, string triggerId, string key,
        JsonElement payload, CancellationToken cancellationToken = default) =>
        provider.GetRequiredService<IStandaloneTriggerIngress<ConsumerSource>>().AdmitAsync(new(triggerId, key, Schema, payload), cancellationToken);
}

public sealed class ConsumerSource;
public sealed class OtherConsumerSource;

public sealed class OtherConsumerContext(StandaloneTriggerInvocationContext trustedContext) : IStandaloneTriggerSourceContext<OtherConsumerSource>
{
    public bool SupportsResourceAuthorization => true;
    public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(trustedContext);
    public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(trigger.Id == signal.TriggerId && trigger.TenantId == trustedContext.Scope.TenantId && trigger.Origin.SourceId == "other-owner");
}

public sealed class ConsumerAuthorization
{
    public bool Allowed { get; set; } = true;
    public bool HasCurrentContext { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string TriggerId { get; set; } = "consumer-trigger";
    public int AuthorizationChecks { get; private set; }
    public void Checked() => AuthorizationChecks++;
}

public sealed class ConsumerContext(StandaloneTriggerInvocationContext trustedContext, ConsumerAuthorization authorization, TimeProvider clock) : IStandaloneTriggerSourceContext<ConsumerSource>
{
    public bool SupportsResourceAuthorization => true;
    public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.HasCurrentContext || authorization.ExpiresAt <= clock.GetUtcNow())
            throw new TriggerOperationException("unauthorized", "The consumer requires a current authorized source context.");
        return ValueTask.FromResult(trustedContext);
    }
    public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); authorization.Checked();
        return ValueTask.FromResult(authorization.Allowed && trigger.Id == authorization.TriggerId && trigger.Id == signal.TriggerId &&
            trigger.TenantId == trustedContext.Scope.TenantId && trigger.TypeKey == ConsumerModule.TypeKey && trigger.Origin.SourceId == "consumer-owner");
    }
}

public sealed class ConsumerTypes : IStandaloneTriggerTypeSource
{
    public IEnumerable<TriggerTypeDescriptor> GetTypes() =>
        [new(ConsumerModule.TypeKey, 1, "Independent source", "Independent", [], [new(["payload", "message"], "Message", "String")])];
}

public sealed class ConsumerValidator : ITriggerConfigurationValidator
{
    public string TypeKey => ConsumerModule.TypeKey;
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(schemaVersion == 1 && configuration.ValueKind == JsonValueKind.Object
            ? [] : [new("consumer-schema", "This module requires configuration schema one.")]);
    }
}

public sealed class ConsumerProbe
{
    public int CapabilityChecks { get; private set; }
    public int PreparedCommands { get; private set; }
    public void CheckedCapability() => CapabilityChecks++;
    public void PreparedCommand() => PreparedCommands++;
}

/// <summary>A normal public target adapter decoration, with no storage or admission shortcut.</summary>
public sealed class ConsumerTarget(IStandaloneTriggerRuntimeTarget selected, ConsumerProbe probe) : IStandaloneTriggerRuntimeTarget
{
    public string ModelKey => selected.ModelKey;
    public bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model)
    { probe.CheckedCapability(); return selected.Supports(nativeWorkflow, model); }
    public ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        CancellationToken cancellationToken = default)
    { probe.PreparedCommand(); return selected.PrepareDispatchAsync(source, recipient, cancellationToken); }
    public ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default) => selected.ReconcileAsync(source, recipient, seal, cancellationToken);
    public ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal,
        CancellationToken cancellationToken = default) => selected.DispatchAsync(source, recipient, seal, cancellationToken);
}

/// <summary>An older third-party participant opts into no new capability by implementing only the original empty contract.</summary>
public sealed class UnsupportedDeliveryStore : IRuntimeIngressDeliveryStore;
