using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.MongoDb;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;

/// <summary>
/// MongoDB counterpart of <see cref="GroundworkWorkflowPortfolioDataSource"/>. MongoDB stores one physical
/// collection per document kind rather than a single shared table, so the SQL implementation's CTE joins
/// become <c>$lookup</c> sub-pipelines here; the entity envelope is preserved as a native BSON sub-document
/// under <c>content</c>, whose field paths mirror the JSON-path segments the SQL implementation extracts.
/// </summary>
public sealed class MongoDbWorkflowPortfolioDataSource(
    Func<IMongoDatabase> databaseFactory,
    IPayloadSerializer payloadSerializer,
    Func<IMongoDatabase>? runtimeDatabaseFactory = null) : IWorkflowPortfolioDataSource
{
    /// <summary>
    /// Upper bound on published definition ids read from the runtime database when the lanes are split.
    /// One row per published definition, so this is catalog scale; exceeding it is reported rather than
    /// silently truncating a count.
    /// </summary>
    private const int SplitPublishedIdLimit = 100_000;

    private const string DefinitionIdPath = "content.entity.id";
    private const string DefinitionTenantIdPath = "content.entity.tenantId";
    private const string DefinitionDeletedAtPath = "content.entity.deletedAt";
    private const string DraftTenantIdPath = "content.entity.tenantId";
    private const string DraftWorkflowDefinitionIdPath = "content.entity.workflowDefinitionId";
    private const string DraftLastModifiedAtPath = "content.entity.lastModifiedAt";
    private const string DraftCreatedAtPath = "content.entity.createdAt";
    private const string DraftIdPath = "content.entity.id";
    private const string ReferenceDefinitionIdPath = "content.reference.definitionId";
    private const string ReferenceScopePath = "content.reference.scope";
    private const string ReferenceDeletedAtPath = "content.reference.deletedAt";
    private const string ReferenceExpiresAtPath = "content.reference.expiresAt";

    private static readonly string DefinitionsCollectionName =
        MongoDbGroundworkNames.CollectionName(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
    private static readonly string DraftsCollectionName =
        MongoDbGroundworkNames.CollectionName(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
    private static readonly string ReferencesCollectionName =
        MongoDbGroundworkNames.CollectionName(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind);

    private readonly JsonSerializerOptions _jsonOptions = GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public bool IsAvailable => true;

    public async ValueTask<WorkflowPortfolioBaseCounts> QueryBaseCountsAsync(
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var database = databaseFactory();
        var definitions = database.GetCollection<BsonDocument>(DefinitionsCollectionName);
        var drafts = database.GetCollection<BsonDocument>(DraftsCollectionName);

        // A $lookup cannot reach a collection in another database, so a split host correlates in memory.
        var (activeCount, publishedCount) = runtimeDatabaseFactory is null
            ? await QueryActiveAndPublishedCountsAsync(definitions, tenantId, asOf, cancellationToken)
            : await QueryActiveAndPublishedCountsAcrossTargetsAsync(definitions, tenantId, asOf, cancellationToken);
        var unpublishedDraftCount = await QueryUnpublishedDraftCountAsync(drafts, tenantId, cancellationToken);

        return new(activeCount, publishedCount, unpublishedDraftCount);
    }

    public async IAsyncEnumerable<WorkflowDefinitionDraft> StreamCurrentDraftsAsync(
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var database = databaseFactory();
        var drafts = database.GetCollection<BsonDocument>(DraftsCollectionName);

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument(DraftTenantIdPath, tenantId)),
            new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = DefinitionsCollectionName,
                ["let"] = new BsonDocument("definitionId", "$" + DraftWorkflowDefinitionIdPath),
                ["pipeline"] = new BsonArray(BuildActiveDefinitionMatchPipeline(tenantId)),
                ["as"] = "activeMatch"
            }),
            new BsonDocument("$match", new BsonDocument("activeMatch.0", new BsonDocument("$exists", true))),
            new BsonDocument("$addFields", new BsonDocument
            {
                ["_lastModifiedInstant"] = new BsonDocument("$toDate", "$" + DraftLastModifiedAtPath),
                ["_createdInstant"] = new BsonDocument("$toDate", "$" + DraftCreatedAtPath)
            }),
            new BsonDocument("$sort", new BsonDocument
            {
                [DraftWorkflowDefinitionIdPath] = 1,
                ["_lastModifiedInstant"] = -1,
                ["_createdInstant"] = -1,
                [DraftIdPath] = -1
            }),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$" + DraftWorkflowDefinitionIdPath,
                ["contentJson"] = new BsonDocument("$first", "$content_json")
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        using var cursor = await drafts.AggregateAsync<BsonDocument>(pipeline, cancellationToken: cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                var contentJson = document["contentJson"].AsString;
                var portfolioDocument = JsonSerializer.Deserialize<PortfolioDraftDocument>(contentJson, _jsonOptions)
                                         ?? throw new InvalidOperationException("A Groundwork workflow draft document could not be deserialized.");
                yield return portfolioDocument.Entity;
            }
        }
    }

    /// <summary>
    /// Counts active definitions on the design database and intersects them with the published references
    /// read from the runtime database.
    /// </summary>
    private async Task<(int ActiveCount, int PublishedCount)> QueryActiveAndPublishedCountsAcrossTargetsAsync(
        IMongoCollection<BsonDocument> definitions,
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        using (var cursor = await definitions.FindAsync(
            new BsonDocument
            {
                [DefinitionTenantIdPath] = tenantId,
                [DefinitionDeletedAtPath] = BsonNull.Value
            },
            new FindOptions<BsonDocument, BsonDocument>
            {
                Projection = new BsonDocument { ["_id"] = 0, [DefinitionIdPath] = 1 }
            },
            cancellationToken))
        {
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var document in cursor.Current)
                {
                    var id = ReadDefinitionId(document);
                    if (id is not null)
                        activeIds.Add(id);
                }
            }
        }

        if (activeIds.Count == 0)
            return (0, 0);

        var references = runtimeDatabaseFactory!().GetCollection<BsonDocument>(ReferencesCollectionName);
        var published = 0;
        var seen = 0;
        using (var cursor = await references.FindAsync(
            new BsonDocument("$and", new BsonArray(BuildPublishedReferenceFilters(asOf))),
            new FindOptions<BsonDocument, BsonDocument>
            {
                Projection = new BsonDocument { ["_id"] = 0, [ReferenceDefinitionIdPath] = 1 }
            },
            cancellationToken))
        {
            var matched = new HashSet<string>(StringComparer.Ordinal);
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var document in cursor.Current)
                {
                    if (++seen > SplitPublishedIdLimit)
                    {
                        throw new InvalidOperationException(
                            $"The workflow portfolio read more than {SplitPublishedIdLimit} published " +
                            "definition references from the runtime target. Correlating the lanes in memory " +
                            "is only viable at catalog scale; co-locate the design and runtime targets.");
                    }

                    var id = ReadReferenceDefinitionId(document);
                    if (id is not null && activeIds.Contains(id))
                        matched.Add(id);
                }
            }

            published = matched.Count;
        }

        return (activeIds.Count, published);
    }

    private static string? ReadDefinitionId(BsonDocument document) => ReadPath(document, DefinitionIdPath);

    private static string? ReadReferenceDefinitionId(BsonDocument document) =>
        ReadPath(document, ReferenceDefinitionIdPath);

    private static string? ReadPath(BsonDocument document, string dottedPath)
    {
        BsonValue current = document;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current is not BsonDocument nested || !nested.TryGetValue(segment, out var next))
                return null;
            current = next;
        }

        return current.IsString ? current.AsString : null;
    }

    private static async Task<(int ActiveCount, int PublishedCount)> QueryActiveAndPublishedCountsAsync(
        IMongoCollection<BsonDocument> definitions,
        string tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                [DefinitionTenantIdPath] = tenantId,
                [DefinitionDeletedAtPath] = BsonNull.Value
            }),
            new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = ReferencesCollectionName,
                ["let"] = new BsonDocument("definitionId", "$" + DefinitionIdPath),
                ["pipeline"] = new BsonArray(BuildPublishedReferenceMatchPipeline(asOf)),
                ["as"] = "publishedMatch"
            }),
            new BsonDocument("$addFields", new BsonDocument("isPublished",
                new BsonDocument("$gt", new BsonArray { new BsonDocument("$size", "$publishedMatch"), 0 }))),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["activeCount"] = new BsonDocument("$sum", 1),
                ["publishedCount"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$isPublished", 1, 0 }))
            })
        };

        var result = await definitions.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline))
            .FirstOrDefaultAsync(cancellationToken);
        return result is null ? (0, 0) : (result["activeCount"].ToInt32(), result["publishedCount"].ToInt32());
    }

    private static async Task<int> QueryUnpublishedDraftCountAsync(
        IMongoCollection<BsonDocument> drafts,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument(DraftTenantIdPath, tenantId)),
            new BsonDocument("$group", new BsonDocument("_id", "$" + DraftWorkflowDefinitionIdPath)),
            new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = MongoDbGroundworkNames.CollectionName(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
                ["let"] = new BsonDocument("definitionId", "$_id"),
                ["pipeline"] = new BsonArray(BuildActiveDefinitionMatchPipeline(tenantId)),
                ["as"] = "activeMatch"
            }),
            new BsonDocument("$match", new BsonDocument("activeMatch.0", new BsonDocument("$exists", true))),
            new BsonDocument("$count", "count")
        };

        var result = await drafts.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline))
            .FirstOrDefaultAsync(cancellationToken);
        return result is null ? 0 : result["count"].ToInt32();
    }

    /// <summary>
    /// Matches an active (non-deleted, tenant-scoped) definition against a <c>$$definitionId</c> let-variable
    /// bound by the enclosing <c>$lookup</c>. Mirrors the SQL implementation's <c>active</c> CTE.
    /// </summary>
    private static BsonDocument[] BuildActiveDefinitionMatchPipeline(string tenantId) =>
    [
        new("$match", new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$" + DefinitionIdPath, "$$definitionId" }),
            new BsonDocument("$eq", new BsonArray { "$" + DefinitionTenantIdPath, tenantId }),
            new BsonDocument("$eq", new BsonArray { "$" + DefinitionDeletedAtPath, BsonNull.Value })
        }))),
        new("$limit", 1)
    ];

    /// <summary>
    /// Matches a live Published reference against a <c>$$definitionId</c> let-variable bound by the enclosing
    /// <c>$lookup</c>. Mirrors the SQL implementation's <c>published</c> CTE; deliberately not tenant-scoped,
    /// exactly like the SQL join (which relies only on the definition-id match against the tenant-scoped
    /// <c>active</c> side).
    /// </summary>
    private static BsonDocument[] BuildPublishedReferenceMatchPipeline(DateTimeOffset asOf) =>
    [
        new("$match", new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$" + ReferenceDefinitionIdPath, "$$definitionId" }),
            new BsonDocument("$eq", new BsonArray { "$" + ReferenceScopePath, (int)WorkflowExecutableReferenceScope.Published }),
            new BsonDocument("$eq", new BsonArray { "$" + ReferenceDeletedAtPath, BsonNull.Value }),
            new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$" + ReferenceExpiresAtPath, BsonNull.Value }),
                new BsonDocument("$gt", new BsonArray
                {
                    new BsonDocument("$toDate", "$" + ReferenceExpiresAtPath),
                    asOf.UtcDateTime
                })
            })
        }))),
        new("$limit", 1)
    ];

    /// <summary>
    /// The same liveness predicate as <see cref="BuildPublishedReferenceMatchPipeline"/>, expressed as plain
    /// filters for the split path where there is no correlated <c>$$definitionId</c> to match against: the
    /// definition-id intersection happens in memory instead.
    /// </summary>
    private static BsonDocument[] BuildPublishedReferenceFilters(DateTimeOffset asOf) =>
    [
        new(ReferenceScopePath, (int)WorkflowExecutableReferenceScope.Published),
        new(ReferenceDeletedAtPath, BsonNull.Value),
        new("$or", new BsonArray
        {
            new BsonDocument(ReferenceExpiresAtPath, BsonNull.Value),
            new BsonDocument("$expr", new BsonDocument("$gt", new BsonArray
            {
                new BsonDocument("$toDate", "$" + ReferenceExpiresAtPath),
                asOf.UtcDateTime
            }))
        })
    ];

    private sealed record PortfolioDraftDocument(
        string Collection,
        WorkflowDefinitionDraft Entity,
        IReadOnlyCollection<DesignMetadataRecord> Layout);
}
