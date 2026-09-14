namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

public sealed class WorkflowExecutableEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string ArtifactHash { get; set; } = null!;
    public string ArtifactIdOrderKey { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public string IncarnationId { get; set; } = null!;
}

public sealed class WorkflowExecutableCoordinationEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}

public sealed class ExecutableActivityTemplateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TemplateId { get; set; } = null!;
    public string TemplateIdHash { get; set; } = null!;
    public string TemplateHash { get; set; } = null!;
    public string TemplateIdOrderKey { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}

public sealed class ExecutableActivityTemplateHashClaimEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TemplateHash { get; set; } = null!;
    public string TemplateHashHash { get; set; } = null!;
    public string TemplateId { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}

public sealed class WorkflowExecutableSourceReferenceEntity
{
    public string Id { get; set; } = null!;
    public string SourceReferenceId { get; set; } = null!;
    public string SourceReferenceIdHash { get; set; } = null!;
    public string SourceReferenceIdOrderKey { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string DefinitionVersionId { get; set; } = null!;
    public string DefinitionVersionIdHash { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public string DefinitionIdHash { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ScopeKeyOrderKey { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public bool IsRetired { get; set; }
    public long ExpiresAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}
