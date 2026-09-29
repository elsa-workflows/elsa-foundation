using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

[EfSchemaContentAddressed("Keyed by scope and artifact id, and the artifact id is the hash of the executable's content (ADR 0038).")]
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

[EfSchemaContentAddressed("An executable activity template is identified by the hash of its content (ADR 0038), which its row projects as TemplateHash.")]
public sealed class ExecutableActivityTemplateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TemplateId { get; set; } = null!;
    public string TemplateIdHash { get; set; } = null!;
    public string TemplateHash { get; set; } = null!;
    public string TemplateHashHash { get; set; } = null!;
    public string TemplateIdOrderKey { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}

[EfSchemaContentAddressed("Keyed by scope and a template's content hash, which its content restates: it is the claim that makes that hash name one template, so a rewrite could forge the claim.")]
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
    public long? ExpiresAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string IncarnationId { get; set; } = null!;
}
