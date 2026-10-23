using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Selects one installed activation strategy by stable Runtime consumer and descriptor schema.
/// </summary>
/// <remarks>
/// A hydrating strategy's activation is the only place a secret-bound input is resolved. The committed snapshot holds
/// a withheld envelope for it; <see cref="ActivitySecretInputResolver"/> resolves the reference for the partition the
/// execution runs under, registers the value with <see cref="IRuntimeSecretMask"/> under the request's activity execution
/// id, converts the text with the plan the envelope carries, and the activity is hydrated from that transient copy of
/// the snapshot. The request's snapshot is never changed, so nothing the activity was hydrated with is written back to
/// state. Every activation resolves again; nothing resolved is kept beyond the mask registration, which the work handler
/// that asked for the activation releases once it has recorded the outcome.
/// </remarks>
public sealed class ActivityActivator(
    IEnumerable<IActivityActivationStrategy> strategies,
    ActivityInputHydrator inputHydrator,
    ActivitySecretInputResolver secretInputResolver,
    IExternalPayloadStore? externalPayloadStore = null) : IActivityActivator
{
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
        var secretInputs = secretInputResolver.Prepare(request.Inputs, strategy.RequiresInputHydration);
        var lease = await strategy.ActivateAsync(
            new ActivityActivationStrategyRequest(request.Contract, descriptor),
            cancellationToken);

        if (!strategy.RequiresInputHydration)
            return lease;

        try
        {
            var inputs = await DereferenceInputsAsync(request.Inputs, cancellationToken);
            if (secretInputs is not null)
                inputs = await secretInputResolver.ResolveAsync(secretInputs, request.WorkflowExecutionId, request.ActivityExecutionId, inputs, cancellationToken);
            inputHydrator.Hydrate(lease.Activity, request.Contract, inputs);
            return lease;
        }
        catch (Exception activationException)
        {
            var canceled = cancellationToken.IsCancellationRequested;
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(lease);
            if (disposalException is not null)
                throw ActivityActivationLeaseDisposer.CombineActivationFailure(activationException, disposalException, canceled);

            throw;
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

    internal static ActivityInputSnapshot WithValues(ActivityInputSnapshot snapshot, IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new(
            snapshot.InvocationId,
            snapshot.ContractFingerprint,
            snapshot.BindingFingerprint,
            values,
            snapshot.MaterializedAt);
}
