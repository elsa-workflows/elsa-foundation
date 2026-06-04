using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionVersion(string definitionId, int version, string? stateSource = null, DateTimeOffset? sourceCreatedAt = null)
    : TenantEntity, IWorkflowDefinitionVersion
{
    /// <summary>
    /// The version number of this workflow definition version
    /// </summary>
    public int Version { get; init; } = version;

    /// <summary>
    /// The id of the workflow definition
    /// </summary>
    public string DefinitionId { get; init; } = definitionId;

    /// <summary>
    /// Navigation property to the <see cref="WorkflowDefinition"/> entity
    /// </summary>
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    [NotMapped]
    public WorkflowDefinitionState State { get; set; } = default!;

    /// <summary>
    /// Shadow property that contains the serialized state of this version. Write-once —
    /// immutability enforced via <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration.
    /// </summary>
    public string? StateSource { get; set; } = stateSource;

    /// <summary>
    /// Timestamp from the external source that authored this version (e.g. a git commit time,
    /// a file mtime, a blob upload time). Populated by provisioners when the source is not the
    /// Design API. <c>null</c> when this version was authored directly through the Design API.
    /// Separate from <see cref="Entity.CreatedAt"/>, which is strictly the DB-side timestamp.
    /// </summary>
    public DateTimeOffset? SourceCreatedAt { get; init; } = sourceCreatedAt;

    IWorkflowDefinition IWorkflowDefinitionVersion.Definition => Definition ?? throw new ArgumentNullException(nameof(Definition));
}
