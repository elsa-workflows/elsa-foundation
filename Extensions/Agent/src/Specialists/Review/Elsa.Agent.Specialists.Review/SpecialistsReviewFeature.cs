using CShells.Features;
using Elsa.Agent.Specialists.Review.Activities;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Agent.Specialists.Review.Services;
using Elsa.Events.Core.Extensions;
using Elsa.Serialization.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Specialists.Review;

/// <summary>
/// A human review wait for any content, typically a user-defined agent's output: the exact native wait, its
/// decision service and the pre-claim resume guard. Revision loops and routing remain ordinary workflow control flow. It needs a current-subject identity provider;
/// without one every decision is refused.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "SpecialistsReview", DisplayName = "Specialists: Human Review",
    Description = "An exact, permission-gated human review wait for agent output or any other content.",
    DependsOn = new object[] { "ActivitiesRuntime", "Events", "Secrets", "Serialization", "Tasks", "FoundationIdentityAbstractions" })]
public class SpecialistsReviewFeature : IShellFeature
{
    /// <summary>How long an issued decision may wait for native admission. Defaults to fifteen minutes.</summary>
    public TimeSpan DecisionLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (DecisionLifetime <= TimeSpan.Zero || DecisionLifetime > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(DecisionLifetime));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new HumanReviewOptions { DecisionLifetime = DecisionLifetime });
        services.TryAddScoped<ReviewDecisionCodec>();
        services.TryAddScoped<IHumanReviews, HumanReviewService>();
        services.AddEventHandler<ActivityResumeValidating, HumanReviewResumeGuard>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, HumanReviewTypesStartupTask>());
    }
}

internal sealed class HumanReviewTypesStartupTask(IWellKnownTypeRegistry types) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        types.RegisterType(typeof(HumanReview), HumanReviewIds.ActivityType);
        return Task.CompletedTask;
    }
}
