using Elsa.Primitives.Exceptions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// The default <see cref="IWorkflowVersionReader"/>: exact versions through the same details composition the version
/// endpoint uses, and a definition's versions through the same store list as the versions endpoint. It decides no authority;
/// the endpoints' permission gate and the consumer's definition authorizer do. Its persistence path is resolved when a read
/// is made, so a consumer can ask whether a reader is installed without building the version stores.
/// </summary>
internal sealed class WorkflowVersionReader(IServiceProvider services) : IWorkflowVersionReader
{
    private IWorkflowVersionDetailsReader details => services.GetRequiredService<IWorkflowVersionDetailsReader>();
    private IWorkflowDefinitionVersionStore versions => services.GetRequiredService<IWorkflowDefinitionVersionStore>();

    public async ValueTask<WorkflowVersionDetails?> ReadAsync(string versionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A synthetic draft identifier is never a persisted version, as the version endpoint refuses it.
        if (string.IsNullOrWhiteSpace(versionId) || versionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var view = await details.ReadAsync(versionId, cancellationToken);
            return new(view.Id, view.Definition.Id, view.Version, view.Definition.Name, view.State.ToState());
        }
        catch (EntityNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask<string?> FindDefinitionIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(versionId) || versionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase)) return null;
        return (await versions.FindByIdAsync(versionId, cancellationToken))?.DefinitionId;
    }

    public async ValueTask<IReadOnlyList<WorkflowDefinitionVersionSummary>> ListAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definitionId)) return [];
        var listed = await versions.ListByDefinitionAsync(definitionId, cancellationToken);
        return listed.Select(version => new WorkflowDefinitionVersionSummary(version.Id, version.Version, version.CreatedAt)).ToArray();
    }
}

/// <summary>Rejects a second <see cref="IWorkflowVersionReader"/> registration at startup (constitution-framework §2.6.2).</summary>
internal sealed class WorkflowVersionReaderStartupTask(IServiceCollection registrations) : IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    /// <summary>Throws when the collection holds more than one version reader registration.</summary>
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IWorkflowVersionReader)) > 1)
            throw new InvalidOperationException("IWorkflowVersionReader has multiple registrations; select one explicit workflow version reader.");
    }
}
