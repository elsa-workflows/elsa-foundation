using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

public interface IAddActivityDefinitionCommand
{
    /// <summary>
    /// Creates an activity definition and its initial version as one logical mutation. The caller owns the
    /// operation key for the complete retry lifetime; implementations return the originally accepted IDs on an
    /// exact replay rather than IDs from a retried request instance.
    /// </summary>
    Task<ActivityDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        ActivityDefinition definition,
        ActivityDefinitionVersion version,
        CancellationToken cancellation = default);
}

/// <summary>The authoritative identity pair produced by creating an activity definition and its first version.</summary>
public sealed record ActivityDefinitionCreated(
    string DefinitionId,
    string VersionId,
    string Version,
    string? Hash);
