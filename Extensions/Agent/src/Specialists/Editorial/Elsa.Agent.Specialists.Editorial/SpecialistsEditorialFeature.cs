using CShells.Features;
using Elsa.Agent.Specialists.Editorial.Activities;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Agent.Specialists.Editorial.Services;
using Elsa.Events.Core.Extensions;
using Elsa.Serialization.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Specialists.Editorial;

/// <summary>
/// The finite editorial example: the deterministic revision gate, the exact native content review wait,
/// its decision service and the pre-claim resume guard. It needs a current-subject identity provider;
/// without one every decision is refused.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "SpecialistsEditorial", DisplayName = "Specialists: Editorial Review",
    Description = "Bounded Copywriter/Proofreader revision routing and an exact, permission-gated human content review wait.",
    DependsOn = new object[] { "ActivitiesRuntime", "Events", "Secrets", "Serialization", "Tasks", "FoundationIdentityAbstractions" })]
public class SpecialistsEditorialFeature : IShellFeature
{
    /// <summary>How long an issued decision may wait for native admission. Defaults to fifteen minutes.</summary>
    public TimeSpan DecisionLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (DecisionLifetime <= TimeSpan.Zero || DecisionLifetime > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(DecisionLifetime));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new EditorialReviewOptions { DecisionLifetime = DecisionLifetime });
        services.TryAddScoped<EditorialDecisionCodec>();
        services.TryAddScoped<IEditorialReviews, EditorialReviewService>();
        services.AddEventHandler<ActivityResumeValidating, EditorialResumeGuard>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, EditorialTypesStartupTask>());
    }
}

internal sealed class EditorialTypesStartupTask(IWellKnownTypeRegistry types) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        types.RegisterType(typeof(ReviewEditorialContent), EditorialReviewIds.ActivityType);
        types.RegisterType(typeof(EditorialRevisionGate), EditorialReviewIds.GateActivityType);
        return Task.CompletedTask;
    }
}
