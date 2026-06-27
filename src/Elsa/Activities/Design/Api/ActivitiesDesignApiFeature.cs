using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api;

[ShellFeature(
    name: "ActivitiesDesignApi",
    Description = "Contains endpoints to manage data in the Activities Design Domain"
)]
public class ActivitiesDesignApiFeature : FastEndpointsFeatureBase
{
    public ActivityAvailabilityOptions ActivityAvailability { get; set; } = new();

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.AddOptions<ActivityAvailabilityOptions>()
            .Configure(options => ApplyFeatureOptions(ActivityAvailability, options));
        services.TryAddSingleton<IActivityAvailabilityEvaluator>(sp =>
            new DefaultActivityAvailabilityEvaluator(sp.GetRequiredService<IOptions<ActivityAvailabilityOptions>>().Value));
        services.TryAddSingleton<IActivityAvailabilitySettingsStore, InMemoryActivityAvailabilitySettingsStore>();

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
