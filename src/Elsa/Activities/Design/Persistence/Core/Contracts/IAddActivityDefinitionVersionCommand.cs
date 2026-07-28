using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core.Design;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

/// <summary>
/// Adds one immutable activity definition version. The operation key is supplied by the caller and remains
/// stable across retries so an implementation can return the authoritative original result without restaging.
/// </summary>
public interface IAddActivityDefinitionVersionCommand
{
    Task<ActivityDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        ActivityDefinitionVersion version,
        CancellationToken cancellationToken = default);
}

/// <summary>The authoritative identity produced by adding an activity definition version.</summary>
public sealed record ActivityDefinitionVersionAdded(
    string DefinitionId,
    string VersionId,
    string Version,
    string? Hash);
