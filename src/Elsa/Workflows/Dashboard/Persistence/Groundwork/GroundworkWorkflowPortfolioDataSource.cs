using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

public sealed class GroundworkWorkflowPortfolioDataSource(
    Func<DbConnection> connectionFactory,
    GroundworkRunHealthDialect dialect,
    IPayloadSerializer payloadSerializer) : IWorkflowPortfolioDataSource
{
    private readonly JsonSerializerOptions _jsonOptions = GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public bool IsAvailable => true;

    public async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {BaseCtes()}
            SELECT (SELECT COUNT(*) FROM active),
                   (SELECT COUNT(*) FROM active a JOIN published p ON p.definition_id = a.definition_id),
                   (SELECT COUNT(*) FROM current_drafts WHERE row_number = 1);
            """;
        AddParameter(command, "tenantId", tenantId);
        AddParameter(command, "asOf", ProviderInstant(asOf));
        AddKinds(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)));
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {BaseCtes(includePublished: false)}
            SELECT content_json FROM current_drafts WHERE row_number = 1 ORDER BY definition_id;
            """;
        AddParameter(command, "tenantId", tenantId);
        AddKinds(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var document = JsonSerializer.Deserialize<PortfolioDraftDocument>(reader.GetString(0), _jsonOptions)
                           ?? throw new InvalidOperationException("A Groundwork workflow draft document could not be deserialized.");
            yield return document.Entity;
        }
    }

    private string BaseCtes(bool includePublished = true)
    {
        var published = includePublished
            ? $"""
              , published AS (
                  SELECT DISTINCT {Json("reference", "definitionId", "r")} AS definition_id
                  FROM groundwork_documents r
                  WHERE r.document_kind = @referenceKind
                    AND {JsonInt("reference", "scope", "r")} = {(int)WorkflowExecutableReferenceScope.Published}
                    AND {Json("reference", "deletedAt", "r")} IS NULL
                    AND ({Json("reference", "expiresAt", "r")} IS NULL OR {Instant(Json("reference", "expiresAt", "r"))} > {Instant("@asOf")})
              )
              """
            : string.Empty;
        return $"""
            WITH active AS (
                SELECT {Json("entity", "id", "d")} AS definition_id
                FROM groundwork_documents d
                WHERE d.document_kind = @definitionKind
                  AND {Json("entity", "tenantId", "d")} = @tenantId
                  AND {Json("entity", "deletedAt", "d")} IS NULL
            ), current_drafts AS (
                SELECT {Json("entity", "workflowDefinitionId", "w")} AS definition_id,
                       w.content_json,
                       ROW_NUMBER() OVER (
                           PARTITION BY {Json("entity", "workflowDefinitionId", "w")}
                           ORDER BY {Instant(Json("entity", "lastModifiedAt", "w"))} DESC,
                                    {Instant(Json("entity", "createdAt", "w"))} DESC,
                                    {Json("entity", "id", "w")} DESC) AS row_number
                FROM groundwork_documents w
                JOIN active a ON a.definition_id = {Json("entity", "workflowDefinitionId", "w")}
                WHERE w.document_kind = @draftKind
                  AND {Json("entity", "tenantId", "w")} = @tenantId
            )
            {published}
            """;
    }

    private string Json(string container, string property, string alias) => dialect == GroundworkRunHealthDialect.Sqlite
        ? $"json_extract({alias}.content_json, '$.{container}.{property}')"
        : $"{alias}.content_json::jsonb #>> '{{{container},{property}}}'";

    private string JsonInt(string container, string property, string alias) => dialect == GroundworkRunHealthDialect.Sqlite
        ? $"CAST({Json(container, property, alias)} AS INTEGER)"
        : $"({Json(container, property, alias)})::integer";

    private string Instant(string expression) => dialect == GroundworkRunHealthDialect.Sqlite
        ? $"julianday({expression})"
        : $"({expression})::timestamptz";

    private object ProviderInstant(DateTimeOffset value) => dialect == GroundworkRunHealthDialect.Sqlite
        ? value.ToUniversalTime().ToString("O")
        : value;

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddKinds(DbCommand command)
    {
        AddParameter(command, "definitionKind", WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        AddParameter(command, "draftKind", WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
        AddParameter(command, "referenceKind", ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
    }

    private sealed record PortfolioDraftDocument(
        string Collection,
        WorkflowDefinitionDraft Entity,
        IReadOnlyCollection<DesignMetadataRecord> Layout);
}
