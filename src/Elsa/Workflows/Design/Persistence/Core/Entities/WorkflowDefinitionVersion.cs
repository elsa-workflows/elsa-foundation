using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionVersion(string definitionId, string version, string? stateSource = null, DateTimeOffset? sourceCreatedAt = null)
    : TenantEntity, IWorkflowDefinitionVersion, IStateSourcedEntity
{
    /// <summary>
    /// Author-controlled SemVer 2.0.0 version string of this workflow definition version.
    /// </summary>
    public string Version { get; init; } = version;

    /// <summary>
    /// Persistence-only normalised key whose ordinal ordering equals SemVer precedence (computed
    /// once via <see cref="SemVer.ToSortKey(string)"/>). Drives DB-side ORDER BY and latest-version
    /// resolution. A domain shadow (§2.9.1): present on the entity, not exposed via the interface.
    /// </summary>
    public string SemVerSortKey { get; init; } = SemVer.ToSortKey(version);

    /// <summary>
    /// The id of the workflow definition
    /// </summary>
    public string DefinitionId { get; init; } = definitionId;

    /// <summary>The exact draft promoted into this immutable version, when authored through Design.</summary>
    public string? SourceDraftId { get; init; }

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

    /// <summary>
    /// Builds the persistence entity from any <see cref="IWorkflowDefinitionVersion"/> (e.g. a
    /// factory read-model). The ctor recomputes <see cref="SemVerSortKey"/>; the saving handler
    /// serialises <see cref="State"/> into <c>StateSource</c>. The <see cref="Definition"/>
    /// navigation is left unset — the write command wires the relationship via <see cref="DefinitionId"/>.
    /// Pass <paramref name="definitionId"/> to rebind the version to a different definition.
    /// </summary>
    public static WorkflowDefinitionVersion From(IWorkflowDefinitionVersion source, string? definitionId = null) =>
        new(definitionId ?? source.DefinitionId, source.Version, sourceCreatedAt: source.SourceCreatedAt)
        {
            Id = source.Id,
            State = source.State,
            SourceDraftId = source.SourceDraftId
        };
}
