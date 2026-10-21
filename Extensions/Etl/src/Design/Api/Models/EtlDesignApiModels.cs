using System.Text.Json.Serialization;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;

namespace Elsa.Etl.Design.Api.Models;

public sealed record EtlPageRequest(string? Cursor = null, int Limit = 50);
public sealed record EtlDefinitionRequest([property: JsonIgnore] string Id);
public sealed record EtlVersionsRequest([property: JsonIgnore] string Id, string? Cursor = null, int Limit = 50);
public sealed record EtlVersionRequest([property: JsonIgnore] string Id, [property: JsonIgnore] int Version);
public sealed record EtlCreateDefinitionRequest(string Id, EtlPipeline Pipeline);
public sealed record EtlUpdateDraftRequest([property: JsonIgnore] string Id, long ExpectedRevision, EtlPipeline Pipeline);
public sealed record EtlValidateRequest([property: JsonIgnore] string Id, long ExpectedRevision);
public sealed record EtlPublishRequest([property: JsonIgnore] string Id, long ExpectedRevision, int DefinitionVersion, string ContentHash);

public sealed record EtlPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record EtlDefinitionSummaryView(string DefinitionId, long Revision, int LastPublishedVersion, DateTimeOffset UpdatedAt);
public sealed record EtlDraftView(string DefinitionId, long Revision, int LastPublishedVersion, EtlPipeline Pipeline, DateTimeOffset UpdatedAt);
public sealed record EtlPublishedVersionView(string DefinitionId, int DefinitionVersion, string ContentHash, string Selection, DateTimeOffset? PublishedAt);
public sealed record EtlPublishedArtifactView(EtlPublishedVersionView Version, EtlPipeline Pipeline);
public sealed record EtlValidationView(long Revision, int DefinitionVersion, string? ContentHash, IReadOnlyList<EtlDiagnostic> Diagnostics);
public sealed record EtlPublicationView(string Status, EtlPublishedVersionView Version);
public sealed record EtlApiProblem(int Status, string Code);

/// <summary>Authoring operations over a trusted request scope. Request DTOs carry no tenant or partition authority.</summary>
public interface IEtlDesignOperations
{
    Task<EtlPage<EtlBindingSummary>> ListBindingsAsync(EtlPageRequest request, CancellationToken cancellationToken);
    Task<EtlPage<EtlDefinitionSummaryView>> ListDefinitionsAsync(EtlPageRequest request, CancellationToken cancellationToken);
    Task<EtlDraftView> CreateAsync(EtlCreateDefinitionRequest request, CancellationToken cancellationToken);
    Task<EtlDraftView> GetAsync(EtlDefinitionRequest request, CancellationToken cancellationToken);
    Task<EtlDraftView> UpdateAsync(EtlUpdateDraftRequest request, CancellationToken cancellationToken);
    Task<EtlValidationView> ValidateAsync(EtlValidateRequest request, CancellationToken cancellationToken);
    Task<EtlPublicationView> PublishAsync(EtlPublishRequest request, CancellationToken cancellationToken);
    Task<EtlPage<EtlPublishedVersionView>> ListVersionsAsync(EtlVersionsRequest request, CancellationToken cancellationToken);
    Task<EtlPublishedArtifactView> GetVersionAsync(EtlVersionRequest request, CancellationToken cancellationToken);
}
