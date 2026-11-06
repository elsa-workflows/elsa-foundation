using CShells.Lifecycle;
using Elsa.Workflows.Testing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Testing.Services;

// Retain the composed registration collection so providers added by later features are checked too.
internal sealed record WorkflowTestingComposition(IServiceCollection Services);

internal sealed class WorkflowTestingCompositionValidator(WorkflowTestingComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireSingle<IWorkflowTestAssertionEvaluator>();
        RequireSingle<IWorkflowTestReportPolicy>();
        RequireSingle<IWorkflowTestReportWriter>();
        return Task.CompletedTask;
    }

    private void RequireSingle<T>()
    {
        var count = composition.Services.Count(x => x.ServiceType == typeof(T) && !x.IsKeyedService);
        if (count != 1)
            throw new InvalidOperationException($"Workflow Tests requires exactly one {typeof(T).Name} registration; found {count}.");
    }

    internal static void Register(IServiceCollection services)
    {
        var previous = services.LastOrDefault(x => x.ServiceType == typeof(WorkflowTestingComposition))
            ?.ImplementationInstance as WorkflowTestingComposition;
        if (ReferenceEquals(previous?.Services, services)) return;
        // A child shell may inherit the metadata and initializer descriptors from its parent.
        // Rebind the check to the actual child collection without adding another initializer.
        services.RemoveAll<WorkflowTestingComposition>();
        services.AddSingleton(new WorkflowTestingComposition(services));
        if (previous is null)
            services.AddShellInitializer<WorkflowTestingCompositionValidator>(LifecyclePhase.Prepare, -200);
    }
}
