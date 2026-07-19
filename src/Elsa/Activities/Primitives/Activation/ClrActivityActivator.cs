using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Activation;

/// <summary>Creates and hydrates one fresh CLR activity in an owned child scope per invocation attempt.</summary>
public sealed class ClrActivityActivator(
    IServiceScopeFactory scopeFactory,
    IWellKnownTypeRegistry typeRegistry,
    IPayloadSerializer payloadSerializer) : IActivityActivationStrategy
{
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.ClrActivity;
    public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
    public bool RequiresInputHydration => true;

    public async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationStrategyRequest request,
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
            return new ActivityActivationLease(activity, scope);
        }
        catch (Exception activationException)
        {
            Exception? disposalException;
            if (activity is not null)
            {
                try
                {
                    await new ActivityActivationLease(activity, scope).DisposeAsync();
                    disposalException = null;
                }
                catch (Exception exception)
                {
                    disposalException = exception;
                }
            }
            else
            {
                try
                {
                    await scope.DisposeAsync();
                    disposalException = null;
                }
                catch (Exception exception)
                {
                    disposalException = exception;
                }
            }

            if (disposalException is not null)
                throw new AggregateException("Activity activation and activation cleanup both failed.", activationException, disposalException);
            throw;
        }
    }
}
