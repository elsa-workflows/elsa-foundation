using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers.Management;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

/// <summary>Authoring and management API; composing it does not start Runtime, ingress listeners or scheduling.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "StandaloneTriggersApi", DisplayName = "Standalone Triggers API", Description = "Manage independent workflow entry points.",
    DependsOn = new object[] { "ApiCapabilities", "Events" })]
public class StandaloneTriggersApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        TriggerAuthoringRegistration.AddServices(services);
        services.TryAddScoped<ITriggerRequestAccess, TriggerRequestAccess>();
        services.TryAddScoped<ITriggerManagementCommitter, AuthoringTriggerCommitter>();
        services.TryAddScoped<TriggerManagement>();
        services.TryAddScoped<TriggerDeliveryOperations>();
        services.AddPermissionContributor<TriggerPermissionContributor>();
        services.AddApiCapabilitySource<StandaloneTriggerCapabilitySource>();
    }
    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => StandaloneTriggersApi.Map(endpoints);
    public virtual void PostConfigureServices(IServiceCollection services)
    {
        var committer = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(ITriggerManagementCommitter));
        if (committer?.ImplementationType == typeof(AuthoringTriggerCommitter) && services.Any(descriptor => descriptor.ServiceType == typeof(ITriggerSourceResourceCommitStore)))
            services.Replace(ServiceDescriptor.Scoped<ITriggerManagementCommitter, ProtectedTriggerManagementCommitter>());
        foreach (var contract in new[] { typeof(IStandaloneTriggerStore), typeof(ITriggerManagementCommitter), typeof(IStandaloneTriggerTypeCatalog),
                     typeof(IStandaloneTriggerTargetDiscovery), typeof(ITriggerInputMapper) })
            if (services.Count(descriptor => descriptor.ServiceType == contract) > 1)
                throw new InvalidOperationException($"Standalone trigger management requires at most one replacement for '{contract.Name}'.");
    }
}

internal sealed class AuthoringTriggerCommitter(IStandaloneTriggerStore store) : ITriggerManagementCommitter
{
    public async ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default)
    {
        var previous = await store.FindAsync(scope, write.Trigger.Id, cancellationToken);
        if (write.Trigger.HasActivated || write.Trigger.DesiredState == TriggerDesiredState.Enabled || write.Subscriptions.Any(row => row.Enabled) ||
            previous?.Trigger.HasActivated == true || previous?.Trigger.DesiredState == TriggerDesiredState.Enabled)
            throw new TriggerOperationException("unavailable", "Protected source activation is not available in this composition.");
        return await store.WriteAsync(scope, write, cancellationToken);
    }
}

internal sealed class StandaloneTriggerCapabilitySource(IServiceProvider services) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ready = services.GetService<IStandaloneTriggerStore>() is not null && services.GetService<IStandaloneTriggerTargetDiscovery>() is not null &&
            services.GetService<ITriggerInputMapper>() is not null;
        if (!ready) return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([]);
        var links = new List<ApiCapabilityLink>
            { new("identity", "runtime/standalone-triggers/identity"), new("types", "runtime/standalone-trigger-types"),
             new("triggers", "runtime/standalone-triggers"), new("trigger", "runtime/standalone-triggers/{triggerId}", templated: true),
             new("subscriptions", "runtime/standalone-trigger-subscriptions"), new("subscription", "runtime/standalone-trigger-subscriptions/{subscriptionId}", templated: true),
             new("targets", "runtime/standalone-trigger-targets"), new("target", "runtime/standalone-trigger-targets/{definitionId}/{slotName}", templated: true),
             new("change-preview", "runtime/standalone-triggers/{triggerId}/changes/preview", templated: true),
             new("change-apply", "runtime/standalone-triggers/{triggerId}/changes/apply", templated: true),
             new("mapping-context", "runtime/standalone-triggers/{triggerId}/mapping-context", templated: true),
             new("mapping-preview", "runtime/standalone-triggers/{triggerId}/mapping-preview", templated: true) };
        if (services.GetService<IRuntimeIngressDeliveryStore>()?.SupportsInspection == true)
        {
            links.Add(new("deliveries", "runtime/standalone-trigger-deliveries"));
            links.Add(new("delivery", "runtime/standalone-trigger-deliveries/{deliveryId}", templated: true));
        }
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([new("elsa.api.standalone-triggers", 1, links, "StandaloneTriggersApi")]);
    }
}
