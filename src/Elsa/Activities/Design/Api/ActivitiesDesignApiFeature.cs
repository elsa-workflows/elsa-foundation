using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ActivitiesDesignApi",
    DisplayName = "Activities Design API",
    Description = "Contains endpoints to manage data in the Activities Design Domain"
)]
public class ActivitiesDesignApiFeature : FastEndpointsFeatureBase
{
    private static readonly string ProcessDependencyCursorSigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public ActivityAvailabilityOptions ActivityAvailability { get; set; } = new();
    public string? DependencyCursorSigningKey { get; set; }
    public int DependencyDefaultPageSize { get; set; } = 100;
    public int DependencyMaximumPageSize { get; set; } = 500;

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddHttpContextAccessor();
        services.TryAddScoped<HttpContextActivityDesignAuthorizationContext>();
        services.TryAddScoped<IActivityAuthoringContext>(sp => sp.GetRequiredService<HttpContextActivityDesignAuthorizationContext>());
        services.TryAddScoped<IActivityDependencyAuthorizationContext>(sp => sp.GetRequiredService<HttpContextActivityDesignAuthorizationContext>());
        services.AddOptions<ActivityAvailabilityOptions>()
            .Configure(options => ApplyFeatureOptions(ActivityAvailability, options));
        services.TryAddSingleton<IActivityAvailabilityEvaluator>(sp =>
            new DefaultActivityAvailabilityEvaluator(sp.GetRequiredService<IOptions<ActivityAvailabilityOptions>>().Value));
        services.TryAddSingleton<IActivityAvailabilityDiagnosticsProjector, DefaultActivityAvailabilityDiagnosticsProjector>();
        services.TryAddSingleton<IActivityAvailabilitySettingsStore, InMemoryActivityAvailabilitySettingsStore>();
        services.TryAddScoped<IActivityUpgradePlanner, ActivityUpgradePlanner>();
        services.TryAddScoped<IActivityUpgradeDiffBuilder, ActivityUpgradeDiffBuilder>();
        services.TryAddSingleton<IActivityProviderRegistry, ActivityProviderRegistry>();
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
        services.TryAddScoped<ActivityDependencyReader>();
        services.TryAddScoped<ReusableActivityAuthoringService>();
        services.TryAddScoped<ActivityVersionLifecycleService>();
        services.TryAddSingleton<IActivityVersionSelectionPolicy, DefaultActivityVersionSelectionPolicy>();

        services.AddEventHandlersFrom(assembly);
        services.AddCommandHandlersFrom(assembly);
        services.AddRequestHandlersFrom(assembly);
    }

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
