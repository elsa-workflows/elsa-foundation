using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Activation;

/// <summary>Creates and hydrates one fresh CLR activity in an owned child scope per invocation attempt.</summary>
public sealed class ClrActivityActivator(
    IServiceScopeFactory scopeFactory,
    IWellKnownTypeRegistry typeRegistry,
    IPayloadSerializer payloadSerializer,
    ActivityInputHydrator hydrator,
    IExternalPayloadStore? externalPayloadStore = null) : IActivityActivator
{
    public async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!StringComparer.Ordinal.Equals(request.Contract.DescriptorKind, typeof(ClrActivityDescriptor).FullName))
            throw new InvalidOperationException($"CLR activation cannot handle descriptor kind '{request.Contract.DescriptorKind}'.");

        var descriptor = payloadSerializer.Deserialize<ClrActivityDescriptor>(request.Contract.DescriptorPayload);
        if (!typeRegistry.TryGetTypeOrDefault(descriptor.TypeAlias, out var activityType))
            throw new UnknownActivityTypeException(descriptor.TypeAlias);
        if (!typeof(IActivity).IsAssignableFrom(activityType))
            throw new InvalidOperationException($"Registered CLR activity type '{activityType.FullName}' does not implement {nameof(IActivity)}.");

        var scope = scopeFactory.CreateAsyncScope();
        IActivity? activity = null;
        try
        {
            activity = (IActivity)ActivatorUtilities.CreateInstance(scope.ServiceProvider, activityType);
            var activationInputs = await DereferenceInputsAsync(request.Inputs, cancellationToken);
            hydrator.Hydrate(activity, request.Contract, activationInputs);
            return new ActivityActivationLease(activity, scope);
        }
        catch
        {
            if (activity is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (activity is IDisposable disposable)
                disposable.Dispose();
            await scope.DisposeAsync();
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
