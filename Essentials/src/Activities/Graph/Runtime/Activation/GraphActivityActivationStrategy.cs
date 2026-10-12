using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Graph.Runtime.Activation;

/// <summary>Creates one fresh graph boundary activity for a pinned <c>elsa.graph-activity/1</c> descriptor.</summary>
public sealed class GraphActivityActivationStrategy(IServiceScopeFactory scopeFactory) : IActivityActivationStrategy
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;
    public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
    public bool RequiresInputHydration => false;

    public async ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = request.Descriptor;
        if (!StringComparer.Ordinal.Equals(descriptor.ConsumerKey, ConsumerKey) ||
            !SupportedSchemaVersions.Contains(descriptor.SchemaVersion, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Graph activation cannot handle consumer/schema '{descriptor.ConsumerKey}/{descriptor.SchemaVersion}'.");
        }
        if (!StringComparer.Ordinal.Equals(request.Contract.DescriptorKind, ConsumerKey))
            throw new InvalidOperationException($"Graph activation cannot handle contract descriptor kind '{request.Contract.DescriptorKind}'.");

        GraphActivityDescriptor payload;
        try
        {
            payload = descriptor.Payload.Deserialize<GraphActivityDescriptor>(SerializerOptions)
                      ?? throw new InvalidOperationException("Graph activity descriptor payload resolved to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Graph activity descriptor payload is invalid for schema 1.", exception);
        }

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var activity = new GraphActivity(
                payload,
                scope.ServiceProvider.GetRequiredService<GraphActivityScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<GraphActivityRecovery>(),
                scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>());
            return new ActivityActivationLease(activity, scope);
        }
        catch (Exception activationException)
        {
            try
            {
                await scope.DisposeAsync();
            }
            catch (Exception disposalException)
            {
                throw new AggregateException(
                    "Graph activity activation and activation-scope disposal both failed.",
                    activationException,
                    disposalException);
            }

            throw;
        }
    }
}
