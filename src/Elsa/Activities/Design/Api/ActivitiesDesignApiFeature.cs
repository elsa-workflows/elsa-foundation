using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Capabilities;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Events.Core.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api;

#pragma warning disable CS0618 // Feature composition must preserve the obsolete host registration.

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ActivitiesDesignApi",
    DisplayName = "Activities Design API",
    Description = "Contains endpoints to manage data in the Activities Design Domain",
    DependsOn = new object[] { "ApiCapabilities", "Expressions" }
)]
public class ActivitiesDesignApiFeature : IWebShellFeature
{
    private static readonly string ProcessDependencyCursorSigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public ActivityAvailabilityOptions ActivityAvailability { get; set; } = new();
    public string? DependencyCursorSigningKey { get; set; }
    public int DependencyDefaultPageSize { get; set; } = 100;
    public int DependencyMaximumPageSize { get; set; } = 500;
    public TimeSpan ForkReservationLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ForkReservationRetention { get; set; } = TimeSpan.FromDays(1);

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var assembly = GetType().Assembly;

        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.TryAddScoped<HttpContextActivityDesignAuthorizationContext>();
        services.TryAddScoped<IActivityAuthoringContext>(sp => sp.GetRequiredService<HttpContextActivityDesignAuthorizationContext>());
        var hasAsyncDependencyHost = services.Any(descriptor => descriptor.ServiceType == typeof(IActivityDependencyContextAsync));
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityDependencyAuthorizationContext)))
            services.AddScoped<IActivityDependencyAuthorizationContext>(sp => sp.GetRequiredService<HttpContextActivityDesignAuthorizationContext>());
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IActivityAuthoringContextAsync)))
        {
            services.AddScoped<IActivityAuthoringContextAsync>(sp =>
                sp.GetRequiredService<IActivityAuthoringContext>() is IActivityAuthoringContextAsync asyncContext
                    ? asyncContext
                    : new LegacyActivityAuthoringContextAdapter(sp.GetRequiredService<IActivityAuthoringContext>()));
        }
        if (!hasAsyncDependencyHost)
            services.AddScoped<IActivityDependencyContextAsync>(sp =>
                sp.GetRequiredService<IActivityDependencyAuthorizationContext>() is IActivityDependencyContextAsync asyncContext
                    ? asyncContext
                    : new LegacyActivityDependencyContextAdapter(sp.GetRequiredService<IActivityDependencyAuthorizationContext>()));
        services.EnsureReplacementContract<IActivityAuthoringContextAsync, HttpContextActivityDesignAuthorizationContext>();
        services.EnsureReplacementContract<IActivityDependencyContextAsync, HttpContextActivityDesignAuthorizationContext>();
        services.AddOptions<ActivityAvailabilityOptions>()
            .Configure(options => ApplyFeatureOptions(ActivityAvailability, options));
        services.TryAddSingleton<IActivityAvailabilityEvaluator>(sp =>
            new DefaultActivityAvailabilityEvaluator(sp.GetRequiredService<IOptions<ActivityAvailabilityOptions>>().Value));
        services.TryAddSingleton<IActivityAvailabilityDiagnosticsProjector, DefaultActivityAvailabilityDiagnosticsProjector>();
        services.TryAddSingleton<IActivityAvailabilitySettingsStore, InMemoryActivityAvailabilitySettingsStore>();
        services.TryAddScoped<ActivityUpgradePlanner>();
        services.TryAddScoped<IActivityUpgradePlanner>(sp => sp.GetRequiredService<ActivityUpgradePlanner>());
        services.TryAddScoped<IActivityUpgradePlanRefresher>(sp => sp.GetRequiredService<ActivityUpgradePlanner>());
        services.TryAddScoped<IActivityUpgradeDiffBuilder, ActivityUpgradeDiffBuilder>();
        services.TryAddSingleton<IActivityProviderRegistry, ActivityProviderRegistry>();
        services.TryAddSingleton<IActivityContractCapabilityCatalog, ExpressionActivityContractCapabilityCatalog>();
        services.TryAddSingleton<ActivityContractAuthoringValidator>();
        services.TryAddSingleton<IActivityTypeKeyPolicy, DefaultActivityTypeKeyPolicy>();
        services.TryAddScoped<IActivityDraftValidator, ActivityDraftValidator>();
        services.TryAddSingleton<IActivityVersionDiffer, ActivityVersionDiffer>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ActivityDependencyCursorOptions>().Configure(options =>
            options.SigningKey = string.IsNullOrWhiteSpace(DependencyCursorSigningKey)
                ? ProcessDependencyCursorSigningKey
                : DependencyCursorSigningKey);
        services.AddOptions<ActivityDependencyReaderOptions>().Configure(options =>
        {
            options.DefaultPageSize = DependencyDefaultPageSize;
            options.MaximumPageSize = DependencyMaximumPageSize;
        });
        services.TryAddSingleton<IActivityDependencyCursorCodec, HmacActivityDependencyCursorCodec>();
        services.TryAddSingleton<IActivityManagementCursorCodec, HmacActivityManagementCursorCodec>();
        services.TryAddSingleton<IActivityForkCandidateIdCodec, HmacActivityForkCandidateIdCodec>();
        services.AddOptions<ActivityForkReservationOptions>().Configure(options =>
        {
            options.Lifetime = ForkReservationLifetime;
            options.Retention = ForkReservationRetention;
        });
        services.TryAddScoped<ActivityDependencyReader>();
        services.TryAddScoped<ReusableActivityAuthoringService>();
        services.TryAddScoped<ActivityForkService>();
        services.TryAddScoped<ActivityDefinitionManagementProjectionService>();
        services.TryAddScoped<ActivityContractProposalService>();
        services.TryAddScoped<ActivityVersionLifecycleService>();
        services.TryAddScoped<ActivityDefinitionRecommendationService>();
        services.TryAddSingleton<IActivityVersionSelectionPolicy, DefaultActivityVersionSelectionPolicy>();
        services.TryAddScoped<IActivityFeatureAttributionResolver, ActivityFeatureAttributionResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBuiltInAuthoringDescriptorProvider, IntrinsicAuthoringDescriptorProvider>());

        // The owner's failure services are keyed so hosts composing several modules keep each
        // module's own error shapes; the endpoint pipeline falls back to unkeyed registrations.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, Endpoints.ActivitiesDesignProblemWriter>(ActivitiesDesignApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, Endpoints.ActivitiesDesignFaultRenderer>(ActivitiesDesignApi.OwnerId);

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
        services.AddApiCapability(ActivityDesignApiCapabilities.StaticDeclaration);
        services.AddPermissionContributor<ActivityDesignPermissionContributor>();
        services.ConfigureHttpJsonOptions(options => ActivitiesDesignJsonOptions.Configure(options.SerializerOptions));
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ActivitiesDesignApi.MapActivitiesDesignApi(endpoints);

    private static void ApplyFeatureOptions(ActivityAvailabilityOptions? source, ActivityAvailabilityOptions target)
    {
        if (source is null)
            return;

        if (source.Sets is { Count: > 0 })
            target.Sets = new Dictionary<string, string[]>(source.Sets, StringComparer.Ordinal);

        ApplyRuleSet(source.Include, target.Include);
        ApplyRuleSet(source.Exclude, target.Exclude);
    }

    private static void ApplyRuleSet(ActivityAvailabilityRuleSet? source, ActivityAvailabilityRuleSet target)
    {
        if (source is null)
            return;

        if (source.ActivityTypes is { Length: > 0 })
            target.ActivityTypes = source.ActivityTypes;

        if (source.Sets is { Length: > 0 })
            target.Sets = source.Sets;
    }
}

#pragma warning restore CS0618
