using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

/// <summary>
/// Serves the workflow-portfolio tile from the Groundwork document tables directly.
/// <para>
/// The tile counts definitions and drafts (design lane) and how many of them are published (runtime lane).
/// When both lanes share a target that is one statement. When a host puts them in different databases there
/// is no single connection that can see both, so the counts are taken per target and correlated in memory.
/// </para>
/// </summary>
/// <param name="connectionFactory">Opens the design lane's database.</param>
/// <param name="runtimeConnectionFactory">
/// Opens the runtime lane's database when it is a different one. Null means the lanes are co-located and
/// the single-statement path applies.
/// </param>
public sealed class GroundworkWorkflowPortfolioDataSource(
    Func<DbConnection> connectionFactory,
    GroundworkRunHealthDialect dialect,
    IPayloadSerializer payloadSerializer,
    Func<DbConnection>? runtimeConnectionFactory = null) : IWorkflowPortfolioDataSource
{
    /// <summary>
    /// Upper bound on published definition ids fetched from the runtime target when the lanes are split.
    /// The set is at most one row per published definition, so this is the catalog's order of magnitude
    /// rather than the execution history's. Exceeding it is reported rather than silently truncating a count.
    /// </summary>
    private const int SplitPublishedIdLimit = 100_000;

    private readonly JsonSerializerOptions _jsonOptions = GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public bool IsAvailable => true;

    public async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        if (runtimeConnectionFactory is not null)
            return await QueryBaseCountsAcrossTargetsAsync(tenantId, asOf, cancellationToken);

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {StatementPrefix}{BaseCtes()}
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
            {StatementPrefix}{BaseCtes(includePublished: false)}
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

    /// <summary>
    /// Counts the portfolio when the design and runtime lanes are in different databases. The design target
    /// supplies the active definition ids and the draft count; the runtime target supplies the ids that have
    /// a live published reference. The intersection is taken here, because no connection can see both.
    /// </summary>
    private async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAcrossTargetsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var activeDefinitionIds = new HashSet<string>(StringComparer.Ordinal);
        int draftCount;

        await using (var designConnection = connectionFactory())
        {
            await designConnection.OpenAsync(cancellationToken);
            await using (var activeCommand = designConnection.CreateCommand())
            {
                activeCommand.CommandText = $"""
                    {StatementPrefix}{BaseCtes(includePublished: false)}
                    SELECT definition_id FROM active;
                    """;
                AddParameter(activeCommand, "tenantId", tenantId);
                AddKinds(activeCommand);
                await using var reader = await activeCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                        activeDefinitionIds.Add(reader.GetString(0));
                }
            }

            await using var draftCommand = designConnection.CreateCommand();
            draftCommand.CommandText = $"""
                {StatementPrefix}{BaseCtes(includePublished: false)}
                SELECT COUNT(*) FROM current_drafts WHERE row_number = 1;
                """;
            AddParameter(draftCommand, "tenantId", tenantId);
            AddKinds(draftCommand);
            draftCount = Convert.ToInt32(await draftCommand.ExecuteScalarAsync(cancellationToken));
        }

        var publishedCount = activeDefinitionIds.Count == 0
            ? 0
            : await CountPublishedAsync(activeDefinitionIds, asOf, cancellationToken);
        return new(activeDefinitionIds.Count, publishedCount, draftCount);
    }

    /// <summary>How many of <paramref name="activeDefinitionIds"/> have a live published reference.</summary>
    private async ValueTask<int> CountPublishedAsync(
        IReadOnlySet<string> activeDefinitionIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        await using var runtimeConnection = runtimeConnectionFactory!();
        await runtimeConnection.OpenAsync(cancellationToken);
        await using var command = runtimeConnection.CreateCommand();
        command.CommandText = $"""
            {StatementPrefix}
            SELECT DISTINCT {Json("reference", "definitionId", "r")} AS definition_id
            FROM groundwork_documents r
            WHERE r.document_kind = @referenceKind
              AND {JsonInt("reference", "scope", "r")} = {(int)WorkflowExecutableReferenceScope.Published}
              AND {Json("reference", "deletedAt", "r")} IS NULL
              AND ({Json("reference", "expiresAt", "r")} IS NULL OR {Instant(Json("reference", "expiresAt", "r"))} > {Instant("@asOf")});
            """;
        AddParameter(command, "asOf", ProviderInstant(asOf));
        AddKinds(command);

        var published = 0;
        var seen = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++seen > SplitPublishedIdLimit)
            {
                throw new InvalidOperationException(
                    $"The workflow portfolio read more than {SplitPublishedIdLimit} published definition ids " +
                    "from the runtime target. Correlating the lanes in memory is only viable at catalog scale; " +
                    "co-locate the design and runtime targets for a catalog this large.");
            }

            if (!reader.IsDBNull(0) && activeDefinitionIds.Contains(reader.GetString(0)))
                published++;
        }

        return published;
    }

    private string Json(string container, string property, string alias) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"json_extract({alias}.content_json, '$.{container}.{property}')",
        GroundworkRunHealthDialect.SqlServer => $"JSON_VALUE({alias}.content_json, '$.{container}.{property}')",
        _ => $"{alias}.content_json::jsonb #>> '{{{container},{property}}}'"
    };

    private string JsonInt(string container, string property, string alias) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"CAST({Json(container, property, alias)} AS INTEGER)",
        GroundworkRunHealthDialect.SqlServer => $"TRY_CAST({Json(container, property, alias)} AS int)",
        _ => $"({Json(container, property, alias)})::integer"
    };

    private string Instant(string expression) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"julianday({expression})",
        GroundworkRunHealthDialect.SqlServer => $"CAST({expression} AS datetimeoffset)",
        _ => $"({expression})::timestamptz"
    };

    private object ProviderInstant(DateTimeOffset value) => dialect == GroundworkRunHealthDialect.Sqlite
        ? value.ToUniversalTime().ToString("O")
        : value;

    // T-SQL requires the statement preceding a CTE to be terminated with a semicolon.
    private string StatementPrefix => dialect == GroundworkRunHealthDialect.SqlServer ? ";" : string.Empty;

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
