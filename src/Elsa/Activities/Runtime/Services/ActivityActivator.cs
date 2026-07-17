using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Selects one installed activation strategy by stable Runtime consumer and descriptor schema.
/// </summary>
public sealed class ActivityActivator(
    IEnumerable<IActivityActivationStrategy> strategies,
    ActivityInputHydrator inputHydrator,
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
        var lease = await strategy.ActivateAsync(
            new ActivityActivationStrategyRequest(request.Contract, descriptor),
            cancellationToken);

        if (!strategy.RequiresInputHydration)
            return lease;

        try
        {
            var inputs = await DereferenceInputsAsync(request.Inputs, cancellationToken);
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

        return new ActivityInputSnapshot(
            snapshot.InvocationId,
            snapshot.ContractFingerprint,
            snapshot.BindingFingerprint,
            values,
            snapshot.MaterializedAt);
    }
}
