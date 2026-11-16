using CShells.Features;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Rules.Workflows.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Rules")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "RulesWorkflowsRuntime", DisplayName = "Rules Native Workflows",
    Description = "Runs durable Rules invocations from native workflow checkpoints.",
    DependsOn = new object[] { "RulesRuntime", "WorkflowsRuntimeResumption" })]
public class RulesWorkflowsRuntimeFeature : IShellFeature
{
    /// <summary>Maximum number of concurrent durable terminal deliveries in this host.</summary>
    [ManifestSetting(DisplayName = "Maximum concurrent deliveries", Description = "Bound on concurrent Rules terminal deliveries, from 1 through 64.", Category = "Runtime")]
    public int MaxConcurrentDeliveries { get; set; } = 2;

    /// <summary>Idle or failed delivery delay.</summary>
    [ManifestSetting(DisplayName = "Poll interval milliseconds", Description = "Delay between delivery attempts without progress, from 10 through 60000 milliseconds.", Category = "Runtime")]
    public int PollIntervalMilliseconds { get; set; } = 250;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (MaxConcurrentDeliveries is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentDeliveries));
        if (PollIntervalMilliseconds is < 10 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(PollIntervalMilliseconds));

        var options = new RulesNativeDeliveryOptions(MaxConcurrentDeliveries,
            TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, RulesCheckpointEnricher>());
        services.AddRuntimePostCommitIntentHandler<RulesIntentHandler>(RulesNativeBoundary.StartKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        services.AddRuntimePostCommitIntentHandler<RulesIntentHandler>(RulesNativeBoundary.CancelKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));

        services.TryAddScoped<RulesNativeDeliveryInspector>();
        services.TryAddScoped<IRuleDeliveryProofValidator, RulesNativeDeliveryProofValidator>();
        services.TryAddScoped<RulesNativeDeliveryExecution>();

        services.TryAddSingleton<RulesNativeDeliveryWorker>();
        services.AddSingleton<IBackgroundTask>(provider => provider.GetRequiredService<RulesNativeDeliveryWorker>());
        services.AddHostedService(provider => provider.GetRequiredService<RulesNativeDeliveryWorker>());
    }
}

internal sealed record RulesNativeDeliveryOptions(int MaxConcurrentDeliveries, TimeSpan PollInterval);
