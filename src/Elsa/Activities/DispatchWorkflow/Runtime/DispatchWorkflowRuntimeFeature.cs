using CShells.Features;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using CShells.Lifecycle;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.DispatchWorkflow.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesDispatchWorkflowRuntime",
    DisplayName = "Activities Dispatch Workflow (Runtime)",
    Description = "Runs pinned DispatchWorkflow child starts through the runtime resumption path.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class DispatchWorkflowRuntimeFeature : IShellFeature
{
    private int _childStartMaxDeliveryAttempts = DispatchWorkflowDeliveryOptions.DefaultMaxDeliveryAttempts;
    private double _childStartRetryDelaySeconds = DispatchWorkflowDeliveryOptions.DefaultRetryDelay.TotalSeconds;

    [ManifestSetting(
        DisplayName = "Maximum nesting depth",
        Description = "Maximum number of cross-workflow dispatch edges permitted from a root execution.",
        Category = "Runtime",
        DefaultValue = "32")]
    public int MaxNestingDepth { get; set; } = DispatchWorkflowOptions.DefaultMaxNestingDepth;

    [ManifestSetting(DisplayName = "Child-start maximum delivery attempts", Description = "Total infrastructure delivery attempts for one committed child start, including the initial attempt.", Category = "Runtime", DefaultValue = "4")]
    public int ChildStartMaxDeliveryAttempts
    {
        get => _childStartMaxDeliveryAttempts;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum child-start delivery attempts must be positive.");

            _childStartMaxDeliveryAttempts = value;
        }
    }

    [ManifestSetting(DisplayName = "Child-start retry delay (seconds)", Description = "Positive delay between child-start infrastructure delivery attempts.", Category = "Runtime", DefaultValue = "1")]
    public double ChildStartRetryDelaySeconds
    {
        get => _childStartRetryDelaySeconds;
        set
        {
            if (value <= 0 || value > TimeSpan.MaxValue.TotalSeconds || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Child-start retry delay must be a finite positive number of seconds.");

            _childStartRetryDelaySeconds = value;
        }
    }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        DispatchWorkflowOptions.ValidateMaxNestingDepth(MaxNestingDepth, nameof(MaxNestingDepth));
        services.Configure<DispatchWorkflowOptions>(options => options.MaxNestingDepth = MaxNestingDepth);
        var deliveryOptions = new DispatchWorkflowDeliveryOptions
        {
            MaxDeliveryAttempts = ChildStartMaxDeliveryAttempts,
            RetryDelay = TimeSpan.FromSeconds(ChildStartRetryDelaySeconds)
        };
        services.Configure<DispatchWorkflowDeliveryOptions>(options =>
        {
            options.MaxDeliveryAttempts = deliveryOptions.MaxDeliveryAttempts;
            options.RetryDelay = deliveryOptions.RetryDelay;
        });
        services.AddRuntimePostCommitIntentHandler<ChildStartExecutor>(
            DispatchWorkflowConstants.StartChildIntentKind,
            new RuntimePostCommitRetryPolicy(deliveryOptions.MaxDeliveryAttempts, deliveryOptions.RetryDelay));
        services.AddRuntimePostCommitIntentHandler<ChildCancelExecutor>(
            DispatchWorkflowConstants.CancelChildIntentKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        services.AddRuntimePostCommitIntentHandler<ParentResumeExecutor>(
            DispatchWorkflowConstants.ResumeParentIntentKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, WorkflowDispatchCancellationEnricher>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, WorkflowDispatchCompletionEnricher>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IPostCommitFailureProjector, WorkflowDispatchDeliveryFailureProjector>());
        services.TryAddSingleton<WorkflowDispatchStagingBuffer>();
        services.TryAddSingleton<IWorkflowDispatchStager>(
            serviceProvider => serviceProvider.GetRequiredService<WorkflowDispatchStagingBuffer>());
        services.TryAddSingleton<IWorkflowDispatchStagingAccessor>(
            serviceProvider => serviceProvider.GetRequiredService<WorkflowDispatchStagingBuffer>());
        services.TryAddScoped<IWorkflowTestScopeCleaner, WorkflowTestScopeCleaner>();
        services.AddSingleton<IShellInitializer, WorkflowDispatchReadinessInitializer>();
    }
}
