using System.Text.Json.Serialization;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Design.Api.Models;

/// <summary>Creates a connector-capable v2 ETL definition.</summary>
public sealed record EtlConnectorCreateDefinitionRequest(string Id, EtlPipelineV2 Pipeline);

/// <summary>Replaces a connector-capable v2 draft at its expected revision.</summary>
public sealed record EtlConnectorUpdateDraftRequest([property: JsonIgnore] string Id, long ExpectedRevision, EtlPipelineV2 Pipeline);

/// <summary>A connector-capable v2 ETL draft.</summary>
public sealed record EtlConnectorDraftView(string DefinitionId, long Revision, int LastPublishedVersion, EtlPipelineV2 Pipeline, DateTimeOffset UpdatedAt);

/// <summary>An immutable published connector-capable v2 ETL artifact.</summary>
public sealed record EtlConnectorPublishedArtifactView(EtlPublishedVersionView Version, EtlPipelineV2 Pipeline);

/// <summary>Authoring operations for connector-capable v2 ETL definitions.</summary>
public interface IEtlConnectorDesignOperations
{
    Task<EtlPage<EtlConnectorBindingSummary>> ListBindingsAsync(EtlPageRequest request, CancellationToken cancellationToken);
    Task<EtlPage<EtlDefinitionSummaryView>> ListDefinitionsAsync(EtlPageRequest request, CancellationToken cancellationToken);
    Task<EtlConnectorDraftView> CreateAsync(EtlConnectorCreateDefinitionRequest request, CancellationToken cancellationToken);
    Task<EtlConnectorDraftView> GetAsync(EtlDefinitionRequest request, CancellationToken cancellationToken);
    Task<EtlConnectorDraftView> UpdateAsync(EtlConnectorUpdateDraftRequest request, CancellationToken cancellationToken);
    Task<EtlValidationView> ValidateAsync(EtlValidateRequest request, CancellationToken cancellationToken);
    Task<EtlPublicationView> PublishAsync(EtlPublishRequest request, CancellationToken cancellationToken);
    Task<EtlPage<EtlPublishedVersionView>> ListVersionsAsync(EtlVersionsRequest request, CancellationToken cancellationToken);
    Task<EtlConnectorPublishedArtifactView> GetVersionAsync(EtlVersionRequest request, CancellationToken cancellationToken);
}
