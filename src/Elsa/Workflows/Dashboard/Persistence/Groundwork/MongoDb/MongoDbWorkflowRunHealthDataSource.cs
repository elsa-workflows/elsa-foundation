using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.MongoDb;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;

/// <summary>
/// Exact bounded Groundwork aggregate over MongoDB. Filtering, incident joining, outcome grouping,
/// current-running count and top-five failures execute inside MongoDB aggregation pipelines; only one
/// row per requested bucket plus five definition rows crosses the application boundary.
/// </summary>
/// <remarks>
/// Unlike the SQL dialects (<see cref="GroundworkWorkflowRunHealthDataSource"/>), MongoDB stores one
/// physical collection per document kind (see <see cref="MongoDbGroundworkNames"/>) instead of a single
/// shared table with a <c>document_kind</c> discriminator, so there is no analogous
/// <c>GroundworkRunHealthDialect</c> member for it. The entity envelope is preserved as a native BSON
/// sub-document under <c>content</c>, whose field paths mirror the JSON-path segments the SQL
/// implementation extracts (for example <c>content.state.tenantId</c> mirrors <c>$.state.tenantId</c>),
/// so the aggregation stages below read as the direct MongoDB counterpart of the SQL CTEs.
/// </remarks>
public sealed class MongoDbWorkflowRunHealthDataSource(
    Func<IMongoDatabase> databaseFactory) : IWorkflowRunHealthDataSource
{
    private const string TenantIdPath = "content.state.tenantId";
    private const string RunKindPath = "content.state.runKind";
    private const string StatusPath = "content.state.status";
    private const string StartedAtPath = "content.state.startedAt";
    private const string WorkflowExecutionIdPath = "content.state.workflowExecutionId";
    private const string IncidentWorkflowExecutionIdPath = "content.workflowExecutionId";
    private const string DefinitionIdPath = "content.state.pinnedExecutable.definitionId";

    private static readonly string ExecutionsCollectionName =
        MongoDbGroundworkNames.CollectionName(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);
    private static readonly string IncidentsCollectionName =
        MongoDbGroundworkNames.CollectionName(ElsaRuntimeStorageManifest.IncidentStateDocumentKind);

    public bool IsAvailable => true;

    public async ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken = default)
    {
        var database = databaseFactory();
        var executions = database.GetCollection<BsonDocument>(ExecutionsCollectionName);
        var incidents = database.GetCollection<BsonDocument>(IncidentsCollectionName);

        var bucketRows = await QueryBucketsAsync(executions, request, cancellationToken);
        var running = await QueryRunningAsync(executions, request.Query, cancellationToken);
        var top = await QueryTopFailuresAsync(executions, request.Query, cancellationToken);
        var buckets = request.Buckets.Select(range => bucketRows.TryGetValue(range.Index, out var counts)
            ? counts.ToSnapshot(range)
            : new WorkflowRunHealthBucket(range.From, range.To, 0, 0, 0, 0, 0, 0, 0)).ToArray();

        return new(
            buckets.Sum(x => x.StartedCount),
            buckets.Sum(x => x.SucceededCount),
            buckets.Sum(x => x.FailedCount),
            buckets.Sum(x => x.CancelledCount),
            buckets.Sum(x => x.IncompleteCount),
            buckets.Sum(x => x.IncidentBearingRunCount),
            buckets.Sum(x => x.IncidentCount),
            running,
            buckets,
            top);
    }

    private static async Task<Dictionary<int, BucketCounts>> QueryBucketsAsync(
        IMongoCollection<BsonDocument> executions,
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken)
    {
        var query = request.Query;
        var switchBranches = new BsonArray(request.Buckets.Select(bucket => new BsonDocument
        {
            ["case"] = new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$gte", new BsonArray { "$startedAtInstant", bucket.From.UtcDateTime }),
                new BsonDocument("$lt", new BsonArray { "$startedAtInstant", bucket.To.UtcDateTime })
            }),
            ["then"] = bucket.Index
        }));

        var results = await executions.Aggregate()
            .Match(Builders<BsonDocument>.Filter.And(CommonFilters(query)))
            .AppendStage<BsonDocument>(new BsonDocument("$addFields",
                new BsonDocument("startedAtInstant", new BsonDocument("$toDate", "$" + StartedAtPath))))
            .Match(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Gte("startedAtInstant", query.From.UtcDateTime),
                Builders<BsonDocument>.Filter.Lt("startedAtInstant", query.To.UtcDateTime)))
            .AppendStage<BsonDocument>(new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = IncidentsCollectionName,
                ["localField"] = WorkflowExecutionIdPath,
                ["foreignField"] = IncidentWorkflowExecutionIdPath,
                ["as"] = "incidentMatches"
            }))
            .AppendStage<BsonDocument>(new BsonDocument("$addFields", new BsonDocument
            {
                ["incidentCount"] = new BsonDocument("$size", "$incidentMatches"),
                ["bucketIndex"] = new BsonDocument("$switch", new BsonDocument
                {
                    ["branches"] = switchBranches,
                    ["default"] = BsonNull.Value
                })
            }))
            .Match(Builders<BsonDocument>.Filter.Ne("bucketIndex", BsonNull.Value))
            .AppendStage<BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$bucketIndex",
                ["startedCount"] = new BsonDocument("$sum", 1),
                ["succeededCount"] = new BsonDocument("$sum", StatusFlag(WorkflowExecutionStatus.Completed)),
                ["failedCount"] = new BsonDocument("$sum", StatusFlag(WorkflowExecutionStatus.Faulted)),
                ["cancelledCount"] = new BsonDocument("$sum", StatusFlag(WorkflowExecutionStatus.Cancelled)),
                ["incompleteCount"] = new BsonDocument("$sum", IncompleteFlag()),
                ["incidentBearingCount"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$gt", new BsonArray { "$incidentCount", 0 }), 1, 0
                })),
                ["incidentCount"] = new BsonDocument("$sum", "$incidentCount")
            }))
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync(cancellationToken);

        return results.ToDictionary(
            document => document["_id"].ToInt32(),
            document => new BucketCounts(
                document["startedCount"].ToInt32(),
                document["succeededCount"].ToInt32(),
                document["failedCount"].ToInt32(),
                document["cancelledCount"].ToInt32(),
                document["incompleteCount"].ToInt32(),
                document["incidentBearingCount"].ToInt32(),
                document["incidentCount"].ToInt32()));
    }

    private static async Task<int> QueryRunningAsync(
        IMongoCollection<BsonDocument> executions,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        var filters = CommonFilters(query);
        filters.Add(Builders<BsonDocument>.Filter.Eq(StatusPath, (int)WorkflowExecutionStatus.Running));
        var count = await executions.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.And(filters),
            cancellationToken: cancellationToken);
        return (int)count;
    }

    private static async Task<IReadOnlyCollection<WorkflowFailureDefinitionSnapshot>> QueryTopFailuresAsync(
        IMongoCollection<BsonDocument> executions,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        var filters = CommonFilters(query);
        filters.Add(Builders<BsonDocument>.Filter.Eq(StatusPath, (int)WorkflowExecutionStatus.Faulted));

        var results = await executions.Aggregate()
            .Match(Builders<BsonDocument>.Filter.And(filters))
            .AppendStage<BsonDocument>(new BsonDocument("$addFields",
                new BsonDocument("startedAtInstant", new BsonDocument("$toDate", "$" + StartedAtPath))))
            .Match(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Gte("startedAtInstant", query.From.UtcDateTime),
                Builders<BsonDocument>.Filter.Lt("startedAtInstant", query.To.UtcDateTime)))
            .AppendStage<BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$" + DefinitionIdPath,
                ["failedCount"] = new BsonDocument("$sum", 1)
            }))
            .Sort(new BsonDocument { ["failedCount"] = -1, ["_id"] = 1 })
            .Limit(5)
            .ToListAsync(cancellationToken);

        return results
            .Select(document => new WorkflowFailureDefinitionSnapshot(document["_id"].AsString, document["failedCount"].ToInt32()))
            .ToArray();
    }

    private static List<FilterDefinition<BsonDocument>> CommonFilters(WorkflowRunHealthQuery query)
    {
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq(TenantIdPath, query.TenantId)
        };
        if (!query.IncludeTestRuns)
            filters.Add(Builders<BsonDocument>.Filter.Ne(RunKindPath, (int)WorkflowRunKind.TestRun));
        return filters;
    }

    private static BsonDocument StatusFlag(WorkflowExecutionStatus status) => new("$cond", new BsonArray
    {
        new BsonDocument("$eq", new BsonArray { "$" + StatusPath, (int)status }),
        1,
        0
    });

    private static BsonDocument IncompleteFlag() => new("$cond", new BsonArray
    {
        new BsonDocument("$in", new BsonArray
        {
            "$" + StatusPath,
            new BsonArray
            {
                (int)WorkflowExecutionStatus.Completed,
                (int)WorkflowExecutionStatus.Faulted,
                (int)WorkflowExecutionStatus.Cancelled
            }
        }),
        0,
        1
    });

    private sealed record BucketCounts(
        int Started,
        int Succeeded,
        int Failed,
        int Cancelled,
        int Incomplete,
        int IncidentBearing,
        int Incidents)
    {
        public WorkflowRunHealthBucket ToSnapshot(WorkflowRunHealthBucketRange range) =>
            new(range.From, range.To, Started, Succeeded, Failed, Cancelled, Incomplete, IncidentBearing, Incidents);
    }
}
