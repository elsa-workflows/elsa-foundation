using Elsa.Primitives.Exceptions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// The default <see cref="IWorkflowExecutableInspection"/>: the read <c>GET runtime/workflows/executables/{artifactId}</c>
/// makes, through the same inspector, with a subset of its response mapped to the Core models. It decides no authority; the endpoint's permission gate and
/// the consumer's own run-access check do.
/// </summary>
internal sealed class WorkflowExecutableInspection(IWorkflowExecutableInspector inspector) : IWorkflowExecutableInspection
{
    public async ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(artifactId)) return null;
        WorkflowExecutableDetailsView view;
        try { view = await inspector.GetAsync(new GetWorkflowExecutable(artifactId), cancellationToken); }
        catch (EntityNotFoundException) { return null; }
        return new(view.ArtifactId, view.ArtifactHash, view.CreatedAt, view.RootActivityType, view.RootActivityVersion, view.NodeCount,
            view.LiveSourceReferenceCount, Node(view.RootActivity),
            view.References.Select(reference => new WorkflowExecutableInspectionSourceReference(reference.SourceReferenceId, reference.Scope,
                reference.SourceKind, reference.SourceId, reference.SourceVersion, reference.DefinitionId, reference.DefinitionVersionId,
                reference.ArtifactVersion, reference.CreatedAt, reference.PublishedAt, reference.ExpiresAt, reference.DeletedAt, reference.Live)).ToArray(),
            view.Dependencies.Select(dependency => new WorkflowExecutableInspectionDependency(dependency.ArtifactId, dependency.ArtifactHash)).ToArray());
    }

    // The endpoint's summary tree: no binding value or source detail.
    private static WorkflowExecutableInspectionNode Node(WorkflowExecutableNodeView node) =>
        new(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion, node.StructureKind,
            node.InputBindings.Select(binding => new WorkflowExecutableInspectionInput(binding.InputName, binding.Source, binding.IsSensitive)).ToArray(),
            node.ChildSlots.Select(slot => new WorkflowExecutableInspectionSlot(slot.Name, slot.Activities.Select(Node).ToArray())).ToArray(),
            node.Connections.Select(connection => new WorkflowExecutableInspectionConnection(connection.Source.NodeId, connection.Source.Port,
                connection.Target.NodeId, connection.Target.Port)).ToArray());
}

/// <summary>Rejects a second <see cref="IWorkflowExecutableInspection"/> registration at startup (constitution-framework §2.6.2).</summary>
internal sealed class WorkflowExecutableInspectionStartupTask(IServiceCollection registrations) : IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    /// <summary>Throws when the collection holds more than one Executable inspection registration.</summary>
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IWorkflowExecutableInspection)) > 1)
            throw new InvalidOperationException("IWorkflowExecutableInspection has multiple registrations; select one explicit workflow Executable inspection.");
    }
}
