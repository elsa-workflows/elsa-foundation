using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Reconciliation.ExecutionProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("Expected: <mount> <parent-artifact-id> <parent-node-id> <child-node-id> <parent-execution-id>.");
            return 2;
        }

        try
        {
            var mount = args[0];
            var parentArtifactId = args[1];
            var parentNodeId = args[2];
            var childNodeId = args[3];
            var parentExecutionId = args[4];

            await using var runtime = BuildRuntimeOnlyEngine(mount);
            runtime.InitializeActivityTypes();
            await using var scope = runtime.Services.CreateAsyncScope();
            var reconciliation = await scope.ServiceProvider
                .GetRequiredService<IWorkflowArtifactReconciler>()
                .ReconcileAsync();
            var importedParent = reconciliation.Entries.Single(entry =>
                StringComparer.Ordinal.Equals(entry.ArtifactId, parentArtifactId));
            if (importedParent.Outcome != WorkflowArtifactImportOutcome.Imported)
                throw new InvalidOperationException($"Parent import was '{importedParent.Outcome}'.");

            var parentReference = await runtime.Services
                .GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                .FindAsync(WorkflowActivationReferenceIdentity.Create(importedParent.ActivationId!))
                ?? throw new InvalidOperationException("The imported parent activation reference was not persisted.");

            await runtime.StartPublishedAsync(parentReference, parentExecutionId);
            await runtime.SweepUntilQuietAsync();
            var parentRun = await runtime.ReadRunAsync(parentExecutionId);
            var dispatchState = parentRun.AssertOutcomes(parentNodeId, DispatchWorkflowOutcomes.Completed);
            var childExecutionId = new WorkflowDispatchIdentity(
                    parentExecutionId,
                    dispatchState.Execution.ActivityExecutionId)
                .ChildWorkflowExecutionId;
            var childRun = await runtime.ReadRunAsync(childExecutionId);
            childRun.AssertCompleted(childNodeId);

            var forbidden = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name)
                .Where(name => name is not null &&
                    (name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal) ||
                     name.StartsWith("Elsa.Workflows.Publishing", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (forbidden.Length != 0)
                throw new InvalidOperationException($"Runtime-only process loaded forbidden assemblies: {string.Join(", ", forbidden)}.");

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Imported = reconciliation.Entries.Count,
                ParentStatus = parentRun.WorkflowState?.Status.ToString(),
                ChildStatus = childRun.WorkflowState?.Status.ToString(),
                ChildExecutionId = childExecutionId,
                ForbiddenAssemblies = forbidden
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static WorkflowExecutionHarness BuildRuntimeOnlyEngine(string mount) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = "fresh-runtime-mount",
                    FolderPath = mount
                }
            }.ConfigureServices(services))
            .ConfigureServices(services => services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)))
            .Build(
                "parent-dispatch-execution",
                "child-probe-execution",
                "child-start-execution",
                "parent-resume-execution");
}
