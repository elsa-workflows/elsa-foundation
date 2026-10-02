using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Selects one installed activation strategy by stable Runtime consumer and descriptor schema.
/// </summary>
/// <remarks>
/// A hydrating strategy's activation is the only place a secret-bound input is resolved. The committed snapshot holds
/// a withheld envelope for it; activation resolves the reference through <see cref="IRuntimeSecretResolver"/> for the
/// partition the execution runs under, converts the text with the plan the envelope carries, and hydrates the
/// activity from a transient copy of the snapshot. The request's snapshot is never changed, so nothing the activity
/// was hydrated with is written back to state. Every activation resolves again; nothing resolved is kept.
/// </remarks>
public sealed class ActivityActivator(
    IEnumerable<IActivityActivationStrategy> strategies,
    ActivityInputHydrator inputHydrator,
    IExternalPayloadStore? externalPayloadStore = null,
    IRuntimeSecretResolver? secretResolver = null,
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
    IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
    IRuntimeValueConversionExecutor? valueConversionExecutor = null) : IActivityActivator
{
    private readonly IRuntimeValueConversionExecutor _valueConversionExecutor = valueConversionExecutor ?? new RuntimeValueConversionExecutor();

    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<IActivityActivationStrategy>> _strategies =
        strategies
            .GroupBy(strategy => strategy.ConsumerKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<IActivityActivationStrategy>)group.ToArray(),
                StringComparer.Ordinal);

    public async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var consumerKey = request.Descriptor?.ConsumerKey ?? request.Contract.DescriptorKind;
        var schemaVersion = request.Descriptor?.SchemaVersion ?? "1";
        if (!_strategies.TryGetValue(consumerKey, out var candidates))
            throw new UnknownActivityConsumerException(consumerKey, schemaVersion);

        var matches = candidates
            .Where(strategy => strategy.SupportedSchemaVersions.Contains(schemaVersion, StringComparer.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            var supported = candidates
                .SelectMany(strategy => strategy.SupportedSchemaVersions)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            throw new UnsupportedActivityDescriptorSchemaException(consumerKey, schemaVersion, supported);
        }
        if (matches.Length > 1)
            throw new InvalidOperationException($"Multiple activity activation strategies claim consumer '{consumerKey}' schema '{schemaVersion}'.");

        var descriptor = request.Descriptor ?? new RuntimeActivityDescriptor(
            consumerKey,
            schemaVersion,
            request.Contract.DescriptorPayload);
        var strategy = matches[0];
        RefuseUnresolvableWithheldInputs(request.Inputs, strategy.RequiresInputHydration);
        var lease = await strategy.ActivateAsync(
            new ActivityActivationStrategyRequest(request.Contract, descriptor),
            cancellationToken);

        if (!strategy.RequiresInputHydration)
            return lease;

        try
        {
            var inputs = await DereferenceInputsAsync(request.Inputs, cancellationToken);
            inputs = await ResolveSecretInputsAsync(request.WorkflowExecutionId, inputs, cancellationToken);
            inputHydrator.Hydrate(lease.Activity, request.Contract, inputs);
            return lease;
        }
        catch (Exception activationException)
        {
            try
            {
                await lease.DisposeAsync();
            }
            catch (Exception disposalException)
            {
                throw new AggregateException(
                    "Activity input hydration and activation cleanup both failed.",
                    activationException,
                    disposalException);
            }

            throw;
        }
    }

    /// <summary>
    /// Refuses, before the activity exists, every withheld input this activation cannot resolve, rather than hydrating
    /// null and letting the activity run on a missing value: any withheld input of a strategy that does not hydrate,
    /// which would otherwise pass it on silently, and a value withheld because its policy requires encryption, which
    /// cannot be recovered. Both fault the activity. Only then is a secret reference in a host that composes no
    /// <see cref="IRuntimeSecretResolver"/> refused, as a missing capability that parks the activity, so an input that
    /// no composition could resolve is never reported as one that composing the resolver would repair.
    /// </summary>
    private void RefuseUnresolvableWithheldInputs(ActivityInputSnapshot snapshot, bool hydratesInputs)
    {
        var withheld = WithheldInputs(snapshot);
        foreach (var (key, value) in withheld)
        {
            if (!hydratesInputs || value.WithheldValue!.Kind != WithheldValueKind.SecretReference)
                throw SecretBindingDiagnostics.WithheldInputNotResolved(key);
        }

        if (withheld.Length > 0 && secretResolver is null)
            throw new RuntimeSecretResolverNotFoundException(withheld[0].Key);
    }

    /// <summary>
    /// Resolves each withheld secret reference into a transient copy of <paramref name="snapshot"/>. After
    /// <see cref="RefuseUnresolvableWithheldInputs"/>, every withheld envelope left is a secret reference and a resolver
    /// is composed. A snapshot without one is returned as it is, without reading the partition or the instance.
    /// </summary>
    private async ValueTask<ActivityInputSnapshot> ResolveSecretInputsAsync(
        string workflowExecutionId,
        ActivityInputSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var secrets = WithheldInputs(snapshot);
        if (secrets.Length == 0)
            return snapshot;

        var tenantId = await ReadExecutingTenantAsync(workflowExecutionId, secrets[0].Value.WithheldValue!.Secret!.Name, cancellationToken);
        var values = new Dictionary<string, ValueEnvelope>(snapshot.Values, StringComparer.Ordinal);
        foreach (var (key, envelope) in secrets)
            values[key] = await ResolveSecretAsync(tenantId, envelope, cancellationToken);

        return WithValues(snapshot, values);
    }

    /// <summary>
    /// The tenant to resolve under is the partition the execution runs under, which is the scope its own rows are stored
    /// in. When the instance records a tenant, it must be that partition: a disagreement refuses with
    /// <see cref="RuntimeSecretResolutionException.TenantMismatch"/> before any secret is read, rather than reading
    /// another tenant's secret. An instance the partition cannot read is refused as well, and a global or across-scope
    /// context has no partition, which the accessor refuses.
    /// </summary>
    private async ValueTask<string> ReadExecutingTenantAsync(
        string workflowExecutionId,
        string referenceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var accessor = partitionAccessor
            ?? throw new InvalidOperationException($"Resolving a secret reference requires an {nameof(IWorkflowExecutionPartitionAccessor)}.");
        var stateStore = workflowExecutionStateStore
            ?? throw new InvalidOperationException($"Resolving a secret reference requires an {nameof(IWorkflowExecutionStateStore)}.");

        var partition = accessor.Current.Value;
        var instance = await stateStore.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' is not found in the partition it runs under, so its secret inputs are not resolved.");
        if (instance.TenantId is { } instanceTenantId && !StringComparer.Ordinal.Equals(instanceTenantId, partition))
            throw new RuntimeSecretResolutionException(referenceName, RuntimeSecretResolutionException.TenantMismatch, isRetryable: false);

        return partition;
    }

    private async ValueTask<ValueEnvelope> ResolveSecretAsync(
        string tenantId,
        ValueEnvelope withheld,
        CancellationToken cancellationToken)
    {
        var reference = withheld.WithheldValue!.Secret!;
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = await secretResolver!.ResolveAsync(new RuntimeSecretResolutionRequest(tenantId, reference), cancellationToken);
        // A resolver that reports a failure because the activation was canceled has not found the secret unusable.
        cancellationToken.ThrowIfCancellationRequested();
        if (!resolution.Succeeded)
            throw new RuntimeSecretResolutionException(reference.Name, resolution.FailureCode!, resolution.IsRetryable);

        return ConvertResolvedSecret(reference.Name, withheld, resolution.Value!);
    }

    /// <summary>
    /// Converts the resolved text with the plan pinned at publish, from a fresh inline envelope: the withheld envelope
    /// itself has no value to convert. A failure, and an envelope that carries no plan, is reported as
    /// <see cref="RuntimeSecretResolutionException.ConversionFailed"/>; the conversion's own exception is dropped
    /// rather than wrapped, because its message may describe the value it rejected. Publish pins only plans from text
    /// that cannot fail on a string, so only an artifact that skipped publish reaches the failure.
    /// </summary>
    private ValueEnvelope ConvertResolvedSecret(string referenceName, ValueEnvelope withheld, string value)
    {
        var plan = withheld.WithheldValue!.ConversionPlan
            ?? throw new RuntimeSecretResolutionException(referenceName, RuntimeSecretResolutionException.ConversionFailed, isRetryable: false);
        try
        {
            return _valueConversionExecutor.Convert(
                ValueEnvelope.Inline(plan.SourceType, JsonSerializer.SerializeToElement(value), withheld.Policy),
                plan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeSecretResolutionException(referenceName, RuntimeSecretResolutionException.ConversionFailed, isRetryable: false);
        }
    }

    private async ValueTask<ActivityInputSnapshot> DereferenceInputsAsync(
        ActivityInputSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Values.Values.All(value => value.ExternalReference is null))
            return snapshot;

        if (externalPayloadStore is null)
            throw new InvalidOperationException("VF-ACT-005: External activity inputs require an IExternalPayloadStore.");

        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        foreach (var (key, value) in snapshot.Values)
        {
            if (value.ExternalReference is null)
            {
                values.Add(key, value);
                continue;
            }

            var payload = await externalPayloadStore.ReadAsync(value.ExternalReference, cancellationToken);
            values.Add(key, ValueEnvelope.Inline(value.Type, payload, value.Policy));
        }

        return WithValues(snapshot, values);
    }

    private static KeyValuePair<string, ValueEnvelope>[] WithheldInputs(ActivityInputSnapshot snapshot) =>
        snapshot.Values
            .Where(item => item.Value.Presence == ValuePresence.Withheld)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

    private static ActivityInputSnapshot WithValues(ActivityInputSnapshot snapshot, IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new(
            snapshot.InvocationId,
            snapshot.ContractFingerprint,
            snapshot.BindingFingerprint,
            values,
            snapshot.MaterializedAt);
}
