using System.Collections.Concurrent;
using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>
/// What the canary's injection seams do, per executable node (spec 188, T098, A15). Every seam is armed for the nodes a
/// scenario names and passes everything else through unchanged, so one host runs every scenario and a scenario's
/// injection reaches its own run only. Registered in the canary host's root services, where both the shell and the
/// tests see it.
/// </summary>
public sealed class CanaryInjections
{
    private readonly ConcurrentDictionary<string, CanaryMaterialization> _materializations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(string? ValueName, bool IsSensitive)>> _captureEverything = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _handlerFaults = new(StringComparer.Ordinal);

    /// <summary>
    /// S10: before the real materializer runs, the <c>SecretRead</c> binding of <paramref name="inputKey"/> on
    /// <paramref name="nodeId"/> is given the policy <c>{IsSensitive: false, RequiresEncryption: false}</c>, the shape a
    /// hand-built imported artifact can already carry (research R13).
    /// </summary>
    public void LowerSecretPolicy(string nodeId, string inputKey) => _materializations[nodeId] = new(LoweredInputKey: inputKey);

    /// <summary>
    /// S6 and S8: after the real materializer withheld <paramref name="inputKey"/> on <paramref name="nodeId"/>, its
    /// envelope is replaced with <paramref name="value"/>, present and inline, under <paramref name="policy"/>.
    /// </summary>
    public void Replant(string nodeId, string inputKey, string value, ValueProtectionPolicy policy) =>
        _materializations[nodeId] = new(Replanted: new(inputKey, value, policy));

    /// <summary>S8b: every payload of <paramref name="nodeId"/> is captured, sensitive or not.</summary>
    public void CaptureEverything(string nodeId) => _captureEverything[nodeId] = new();

    /// <summary>Every value of <paramref name="nodeId"/> the capture-everything policy captured, with its sensitivity.</summary>
    public IReadOnlyCollection<(string? ValueName, bool IsSensitive)> CapturedEverything(string nodeId) =>
        _captureEverything.TryGetValue(nodeId, out var captured) ? captured.ToArray() : [];

    /// <summary>S9: once the invoke handler has handled <paramref name="nodeId"/>'s invocation, an exception whose message is <paramref name="value"/> escapes it.</summary>
    public void FaultInvokeHandler(string nodeId, string value) => _handlerFaults[nodeId] = value;

    internal CanaryMaterialization? MaterializationFor(string nodeId) => _materializations.GetValueOrDefault(nodeId);

    internal bool CapturesEverything(RuntimePayloadCaptureRequest request)
    {
        if (!request.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) || !_captureEverything.TryGetValue(nodeId, out var captured))
            return false;
        captured.Enqueue((request.ValueName, request.IsSensitive));
        return true;
    }

    internal string? HandlerFaultFor(string nodeId) => _handlerFaults.GetValueOrDefault(nodeId);
}

internal sealed record CanaryMaterialization(string? LoweredInputKey = null, CanaryReplant? Replanted = null);

internal sealed record CanaryReplant(string InputKey, string Value, ValueProtectionPolicy Policy);

/// <summary>
/// The materializer decorator (T098): lowers a secret read's policy before the real materializer runs (S10), or puts a
/// planted value where the real materializer withheld one (S6, S8). Every other node is materialized unchanged.
/// </summary>
public sealed class CanaryInputMaterializer(IRuntimeActivityInputMaterializer inner, CanaryInjections injections) : IRuntimeActivityInputMaterializer
{
    private static readonly ValueTypeDescriptor Text = new("String");

    public async ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(
        ExecutableNode node,
        string invocationId,
        RuntimeInputBindingResolutionContext resolutionContext,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken = default)
    {
        if (injections.MaterializationFor(node.ExecutableNodeId) is not { } injection)
            return await inner.MaterializeSnapshotAsync(node, invocationId, resolutionContext, materializedAt, cancellationToken);

        var materializedNode = injection.LoweredInputKey is { } loweredKey ? Lowered(node, loweredKey) : node;
        var snapshot = await inner.MaterializeSnapshotAsync(materializedNode, invocationId, resolutionContext, materializedAt, cancellationToken);
        return injection.Replanted is { } replant ? Replanted(snapshot, replant) : snapshot;
    }

    private static ExecutableNode Lowered(ExecutableNode node, string inputKey)
    {
        var binding = node.InputBindings[inputKey];
        if (binding.Source != RuntimeInputBindingSource.SecretRead)
            throw new InvalidOperationException($"The canary lowers a secret read only; input '{inputKey}' is bound to {binding.Source}.");

        var policy = binding.EffectivePolicy;
        var lowered = new RuntimeInputBinding(
            binding.InputKey,
            binding.TargetType,
            new ValueProtectionPolicy(policy.Lifecycle, policy.Storage, isSensitive: false, requiresEncryption: false, policy.RedactionMode, policy.RetentionPolicy, policy.Metadata),
            binding.Source,
            metadata: binding.Metadata,
            conversionPlan: binding.ConversionPlan,
            secret: binding.Secret);
        var bindings = node.InputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        bindings[inputKey] = lowered;
        return new ExecutableNode(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.DescriptorType,
            node.DescriptorPayload,
            bindings,
            node.Metadata,
            node.ChildSlots,
            node.Structure,
            node.ActivityContract,
            node.IntrinsicKind,
            node.IntrinsicVariable,
            node.OutputCaptures,
            node.DescriptorSchemaVersion);
    }

    private static ActivityInputSnapshot Replanted(ActivityInputSnapshot snapshot, CanaryReplant replant)
    {
        if (snapshot.Values[replant.InputKey].Presence != ValuePresence.Withheld)
            throw new InvalidOperationException($"The canary replants a withheld value only; input '{replant.InputKey}' was not withheld.");

        var values = snapshot.Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        values[replant.InputKey] = ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement(replant.Value), replant.Policy);
        return new ActivityInputSnapshot(snapshot.InvocationId, snapshot.ContractFingerprint, snapshot.BindingFingerprint, values, snapshot.MaterializedAt);
    }
}

/// <summary>The capture-everything payload capture policy double (T098, S8b): it captures every payload of an armed node in full.</summary>
public sealed class CanaryPayloadCapturePolicy(IRuntimePayloadCapturePolicy inner, CanaryInjections injections) : IRuntimePayloadCapturePolicy
{
    public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
        injections.CapturesEverything(request)
            ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "The canary captures every payload of this node.")
            : inner.Decide(request);
}

/// <summary>
/// The work-handler decorator (T098, S9): once the invoke handler has handled an armed node's invocation, an exception
/// whose message is the planted value escapes it, as it would from a handler that let an unmasked exception out. The
/// drainer records it as a handler fault.
/// </summary>
public sealed class CanaryFaultingInvokeHandler(IWorkflowSchedulerWorkHandler inner, CanaryInjections injections) : IWorkflowSchedulerWorkHandler
{
    public string Name => inner.Name;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        await inner.HandleAsync(workItem, cancellationToken);
        var nodeId = workItem.Payload?.Deserialize<RuntimeInvokeActivityCommandPayload>()?.ExecutableNodeId;
        if (nodeId is not null && injections.HandlerFaultFor(nodeId) is { } planted)
            throw new InvalidOperationException(planted);
    }
}

/// <summary>
/// The canary host's diagnostics settings accessor: the settings saved through the runtime API, resolved under a host
/// policy that allows full payloads, so the canary's <c>Payload</c> pass captures at <c>Payload</c>. The runtime's own
/// accessor resolves every saved setting under the default host policy, which caps capture at
/// <c>DiagnosticSnapshot</c>, so without this replacement the <c>Payload</c> pass would repeat the
/// <c>DiagnosticSnapshot</c> one.
/// </summary>
public sealed class CanaryDiagnosticsSettingsAccessor(InMemoryRuntimeDiagnosticsSettingsStore store) : IRuntimeDiagnosticsSettingsAccessor
{
    private static readonly RuntimeDiagnosticsHostPolicy FullPayloads = new()
    {
        MaximumLevel = RuntimeDiagnosticsEvidenceLevel.Payload,
        SubjectMaximums = new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal),
        LimitationReasons = []
    };

    public RuntimeDiagnosticsSettingsView Current =>
        RuntimeDiagnosticsSettingsResolver.Resolve(store.Current.Requested, FullPayloads, new RuntimeDiagnosticsPermissions { CanEnableFullPayloads = true });

    public RuntimeDiagnosticsEvidenceLevel GetEffectiveLevel(RuntimePayloadCaptureSubject subject) =>
        RuntimeDiagnosticsSettingsResolver.GetEffectiveLevel(Current, subject);
}

/// <summary>
/// Composes the canary's injection seams into the canary host's shell, and nowhere else (spec 188, T098): each is a
/// replacement of a runtime contract's registration, made after every feature has registered its own. No production
/// code gains a switch; a seam that is not armed for a node passes it through unchanged.
/// </summary>
[ShellFeature(
    name: Name,
    DisplayName = "Secrets canary injection seams",
    Description = "Test-only injection seams of the spec 188 canary.",
    DependsOn = new object[] { "ActivitiesRuntime", "WorkflowsRuntimeApi" })]
public sealed class SecretsCanaryInjectionFeature : IShellFeature, IPostConfigureShellServices
{
    public const string Name = "SecretsCanaryInjection";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        Decorate<IRuntimeActivityInputMaterializer>(services, (provider, inner) => new CanaryInputMaterializer(inner, provider.GetRequiredService<CanaryInjections>()));
        Decorate<IRuntimePayloadCapturePolicy>(services, (provider, inner) => new CanaryPayloadCapturePolicy(inner, provider.GetRequiredService<CanaryInjections>()));
        services.Replace(ServiceDescriptor.Singleton<IRuntimeDiagnosticsSettingsAccessor>(provider =>
            new CanaryDiagnosticsSettingsAccessor(provider.GetRequiredService<InMemoryRuntimeDiagnosticsSettingsStore>())));

        var invokeHandler = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) && descriptor.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        services.Remove(invokeHandler);
        services.AddSingleton<IWorkflowSchedulerWorkHandler>(provider => new CanaryFaultingInvokeHandler(
            ActivatorUtilities.CreateInstance<WorkflowInvokeActivitySchedulerWorkHandler>(provider),
            provider.GetRequiredService<CanaryInjections>()));
    }

    /// <summary>Replaces the one registration of <typeparamref name="TService"/> with <paramref name="decorate"/> over what it registered.</summary>
    private static void Decorate<TService>(IServiceCollection services, Func<IServiceProvider, TService, TService> decorate) where TService : class
    {
        var registration = services.Single(descriptor => descriptor.ServiceType == typeof(TService));
        services.Remove(registration);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)Create(provider, registration)),
            registration.Lifetime));
    }

    private static object Create(IServiceProvider provider, ServiceDescriptor registration) =>
        registration.ImplementationInstance
        ?? registration.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!);
}
