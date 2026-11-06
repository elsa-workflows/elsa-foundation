using CShells.Features;
using Elsa.Events.Core.Contracts;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Jobs.Workflows.Runtime;

/// <summary>Optional native integration; handler execution remains with an independently configured Jobs worker.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs"), ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "JobsWorkflowsRuntime", DisplayName = "Jobs Native Workflows",
    Description = "Awaits or independently submits durable Jobs through native checkpoints.", DependsOn = new object[] { "Tasks", "WorkflowsRuntimeResumption" })]
public class JobsWorkflowsRuntimeFeature : IShellFeature
{
    public string PersistencePartition { get; set; } = "default";
    public string? TenantId { get; set; }
    public bool EnableResultDelivery { get; set; } = true;
    public int DeliveryPageSize { get; set; } = 32;
    public int PollIntervalMilliseconds { get; set; } = 250;
    public int OperationTimeoutSeconds { get; set; } = 30;
    public virtual void ConfigureServices(IServiceCollection services) => services.AddJobsWorkflowsRuntime(options =>
    {
        options.EnableResultDelivery = EnableResultDelivery;
        if (EnableResultDelivery) options.DeliveryScopes.Add(new(PersistencePartition, TenantId));
        options.DeliveryPageSize = DeliveryPageSize; options.PollInterval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
        options.OperationTimeout = TimeSpan.FromSeconds(OperationTimeoutSeconds);
    });
}

/// <summary>Plain-host composition of the same saved native boundary used by the optional shell feature.</summary>
public static class JobsWorkflowsRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddJobsWorkflowsRuntime(this IServiceCollection services, Action<JobWorkflowRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobWorkflowRuntimeSettings))) throw new InvalidOperationException("Jobs native runtime is already configured.");
        var options = new JobWorkflowRuntimeOptions(); configure?.Invoke(options);
        var settings = JobWorkflowRuntimeSettings.Capture(options); services.AddSingleton(settings); services.TryAddSingleton(TimeProvider.System); services.AddLogging();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, JobsCheckpointEnricher>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, JobsSubmissionCheckpointEnricher>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<ActivityResumeValidating>, JobsNativeResumeValidator>());
        services.AddRuntimePostCommitIntentHandler<JobsNativeIntentHandler>(JobsNativeBoundary.StartKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<JobsNativeIntentHandler>(JobsNativeBoundary.CancelKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<JobsNativeSettlementIntentHandler>(JobsNativeBoundary.SettlementKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<JobsNativeSubmissionIntentHandler>(JobsNativeSubmissionBoundary.StartKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<JobsNativeSubmissionIntentHandler>(JobsNativeSubmissionBoundary.CancelKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<JobsNativeSubmissionIntentHandler>(JobsNativeSubmissionBoundary.OwnershipKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true);
        services.TryAddScoped<JobsNativeSourceVerifier>();
        services.TryAddScoped<JobsNativeDeliveryInspector>();
        services.TryAddScoped<IJobNativeDeliveryInspector>(provider => provider.GetRequiredService<JobsNativeDeliveryInspector>());
        services.TryAddScoped<JobsNativeSubmissionInspector>();
        services.TryAddScoped<IJobNativeSubmissionInspector>(provider => provider.GetRequiredService<JobsNativeSubmissionInspector>());
        services.TryAddScoped<JobsResultDeliveryExecution>();
        services.TryAddScoped<JobsNativeResultSettlement>();
        services.TryAddScoped<IJobNativeResultSettlement>(provider => provider.GetRequiredService<JobsNativeResultSettlement>());
        services.AddSingleton<JobsNativeConfigurationValidator>();
        services.AddSingleton<IStartupTask>(provider => provider.GetRequiredService<JobsNativeConfigurationValidator>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<JobsNativeConfigurationValidator>());
        if (settings.EnableResultDelivery)
        {
            services.AddSingleton<JobsResultDeliveryWorker>();
            services.AddSingleton<IBackgroundTask>(provider => provider.GetRequiredService<JobsResultDeliveryWorker>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<JobsResultDeliveryWorker>());
        }
        return services;
    }
}

internal sealed class JobsNativeConfigurationValidator(IServiceScopeFactory scopes) : IHostedService, IStartupTask
{
    Task ITask.ExecuteAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope(); cancellationToken.ThrowIfCancellationRequested();
        if (scope.ServiceProvider.GetServices<IJobCommands>().Take(2).Count() != 1 || scope.ServiceProvider.GetServices<IJobQueries>().Take(2).Count() != 1 ||
            scope.ServiceProvider.GetServices<IJobNativeDeliveryInspector>().Take(2).Count() != 1 ||
            scope.ServiceProvider.GetServices<IJobNativeSubmissionInspector>().Take(2).Count() != 1 ||
            scope.ServiceProvider.GetServices<IJobNativeResultSettlement>().Take(2).Count() != 1)
            throw new JobException("jobs.native.authority-invalid", "Jobs native integration requires exactly one command, query, native-evidence and settlement authority.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
