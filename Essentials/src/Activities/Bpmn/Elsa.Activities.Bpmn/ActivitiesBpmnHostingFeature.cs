using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Bpmn;

/// <summary>Direct BPMN execution over the shared runtime authority; compose explicitly with qualified persistence.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(name: "ActivitiesBpmnHosting", DisplayName = "Direct BPMN hosting",
    Description = "Direct BPMN model execution through the durable runtime.", DependsOn = new object[] { "Events", "WorkflowsRuntimeResumption" })]
public class ActivitiesBpmnHostingFeature : IShellFeature
{
    /// <summary>Opt in to new execution admission; existing readers and workers stay installed when disabled.</summary>
    public bool AdmitNewExecutions { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddRuntimeModelHosting();
        services.TryAddScoped<BpmnDirectExecutionDriver>();
        services.TryAddScoped<BpmnNativeWorkBoundary>();
        services.TryAddScoped<BpmnNativeWorkDelivery>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, BpmnNativeWorkCheckpointEnricher>());
        foreach (var kind in new[] { BpmnNativeWorkBoundary.StartKind, BpmnNativeWorkBoundary.ResultKind })
            services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(kind, typeof(BpmnNativeWorkDelivery),
                RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), requiresDurableSource: true));
        services.TryAddSingleton(new BpmnModelHostingOptions(AdmitNewExecutions));
        services.TryAddScoped<IBpmnModelRuntime, BpmnModelRuntime>();
        services.AddSingleton<IShellInitializer>(new BpmnModelHostingRegistrationInitializer(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeExecutionProtocolContributor, BpmnProtocolContributor>());
    }

    private sealed class BpmnProtocolContributor : IRuntimeExecutionProtocolContributor
    {
        public BpmnProtocolContributor() { }
        public IEnumerable<RuntimeExecutionProtocolRegistration> GetProtocols() =>
            [new(new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), typeof(BpmnDirectExecutionDriver))
                { InputKinds = ["start", "complete-human", "native-result"] }];
    }
}

internal sealed record BpmnModelHostingOptions(bool AdmitNewExecutions);

internal sealed class BpmnModelHostingRegistrationInitializer(IServiceCollection registrations) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var services = registrations.Where(item => item.ServiceType == typeof(IBpmnModelRuntime)).ToArray();
        if (services is not [{ Lifetime: ServiceLifetime.Scoped }])
            throw new InvalidOperationException("BPMN model hosting requires exactly one scoped IBpmnModelRuntime.");
        return Task.CompletedTask;
    }
}
