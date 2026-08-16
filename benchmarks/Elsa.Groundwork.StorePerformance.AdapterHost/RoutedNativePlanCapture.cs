using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the routed native-plan leaves for the E3 benchmark workloads through the same public runtime
/// stores that the timed adapters use. The first implementation is deliberately SQLite-only: an unsupported
/// provider is an explicit operator action, never an opportunity to emit synthetic evidence.
/// </summary>
internal static class RoutedNativePlanCapture
{
    private const string Scope = "e3-native-plan-capture";
    private const int AcceptanceCardinality = 100_000;
    private const int QueryLimit = 32;
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> RequiredRouteIdentities(PerformanceWorkload workload) =>
        Definitions(workload.Id).Select(definition => definition.Identity).ToArray();

    public static async Task<IReadOnlyList<NativeRouteEvidence>> CaptureAsync(
        PerformanceWorkload workload,
        string provider,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!string.Equals(provider, "sqlite", StringComparison.Ordinal))
        {
            throw new PerformanceContractException(
                $"Routed native-plan capture for '{workload.Id}' currently supports SQLite only; provider '{provider}' " +
                "must use a provider-specific capture leaf before it can be admitted. No evidence was synthesized.");
        }

        var definitions = Definitions(workload.Id);
        if (!workload.RequiredNativeRoutes.SequenceEqual(definitions.Select(definition => definition.Identity), StringComparer.Ordinal))
        {
            throw new PerformanceContractException(
                $"The routed capture catalog for '{workload.Id}' does not match its frozen requiredNativeRoutes contract.");
        }

        await using var driver = GroundworkProviderDriverFactory.Create(provider);
        await driver.InitializeAsync(cancellationToken);
        await driver.ResetPhysicalAsync(cancellationToken);
        await using var client = await driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope(Scope)),
            cancellationToken);
        var physicalStore = client.BoundedDocumentStore
                           ?? throw new PerformanceContractException(
                               "The SQLite provider did not expose its admitted bounded document-store runtime.");
        var explainer = client.PhysicalDocumentQueryExplainer
                        ?? throw new PerformanceContractException(
                            "The SQLite provider did not expose its admitted native query explainer.");
        var capturedStore = new CapturingBoundedDocumentStore(physicalStore);
        await using var services = BuildServices(client.DocumentStore, capturedStore);

        var observations = new List<RouteObservation>(definitions.Length);
        foreach (var definition in definitions)
        {
            capturedStore.Clear();
            var materialized = await definition.Invoke(services, cancellationToken);
            var query = capturedStore.RequireQuery(definition.Identity);
            var plan = explainer.ResolvePlan(query, query.ResultOperation);
            observations.Add(new RouteObservation(definition, query, plan, materialized));
        }

        Directory.CreateDirectory(outputDirectory);
        var evidence = new List<NativeRouteEvidence>(observations.Count);
        foreach (var observation in observations)
        {
            var request = CreateRequest(observation.Query, observation.Plan);
            // Each provider driver owns one physical evidence table per request. Preparing immediately
            // before the corresponding public invocation keeps routes on the same shared table isolated
            // without weakening the driver's exact dataset contract.
            await driver.PrepareNativeRoutePlanDatasetAsync([request], cancellationToken);
            capturedStore.Clear();
            var materialized = await observation.Definition.Invoke(services, cancellationToken);
            var query = capturedStore.RequireQuery(observation.Definition.Identity);
            if (!string.Equals(query.QueryIdentity, observation.Query.QueryIdentity, StringComparison.Ordinal) ||
                !string.Equals(query.DocumentKind, observation.Query.DocumentKind, StringComparison.Ordinal))
            {
                throw new PerformanceContractException(
                    $"Public route '{observation.Definition.Identity}' changed its query identity or document kind after dataset preparation.");
            }

            var explanation = await explainer.ExplainAsync(query, cancellationToken);
            request = CreateRequest(query, explanation.Plan);
            var result = await driver.CaptureNativeRoutePlanAsync(
                request,
                explanation,
                materialized,
                cancellationToken);
            var rawReference = RawPlanReference(workload.Id, provider, observation.Definition.Identity);
            var rawPath = Path.Combine(outputDirectory, rawReference);
            WriteRawPlans(rawPath, explanation.Commands);
            evidence.Add(new NativeRouteEvidence(
                observation.Definition.Identity,
                rawReference,
                NativePlanEvidenceStaging.Sha256(rawPath),
                result.PlanClassification,
                result.IndexName,
                checked((int)result.PhysicalCardinality),
                result.HasStorageScopePredicate,
                result.HasRoutePredicate,
                result.FiniteLimit ?? throw new PerformanceContractException(
                    $"Routed query '{observation.Definition.Identity}' did not expose a finite provider limit."),
                result.MaterializedCandidateCount));
        }

        return evidence;
    }

    private static ServiceProvider BuildServices(
        IDocumentStore documentStore,
        IBoundedDocumentStore boundedStore)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        collection.AddSingleton<IPersistenceAccessContextAccessor>(GroundworkTestAccess.AccessContext(Scope));
        collection.AddSingleton(documentStore);
        collection.AddSingleton(boundedStore);
        collection.AddWorkflowRuntime();
        collection.AddGroundworkRuntimeStores();
        return collection.BuildServiceProvider();
    }

    private static GroundworkNativeRoutePlanRequest CreateRequest(
        DocumentQuery query,
        PhysicalQueryPlan plan)
    {
        var routeComparison = query.Clauses
            .SelectMany(clause => clause.Comparisons)
            .Select(comparison => (Comparison: comparison, Predicate: plan.Predicates.FirstOrDefault(predicate =>
                string.Equals(predicate.Path, comparison.Path, StringComparison.Ordinal) &&
                !string.Equals(predicate.Field.Identifier, "storage_scope", StringComparison.Ordinal))))
            .FirstOrDefault(pair => pair.Predicate is not null);
        if (routeComparison.Predicate is null || routeComparison.Comparison.Values.FirstOrDefault() is not { } routeValue)
        {
            throw new PerformanceContractException(
                $"Public route '{query.QueryIdentity}' exposed no non-scope route predicate suitable for native capture.");
        }

        var projected = plan.Predicates
            .Where(predicate => !string.Equals(predicate.Field.Identifier, "storage_scope", StringComparison.Ordinal))
            .Select(predicate => predicate.Field)
            .Concat(plan.Order
                .Where(order => !order.IsIdentityTieBreak)
                .Select(order => order.Field))
            .Distinct()
            .ToArray();
        var projectedValues = projected
            .Select(field => ProjectedValue(field, query, plan))
            .ToArray();
        var routeField = routeComparison.Predicate.Field.Identifier;
        var varying = projected
            .Where(field => !string.Equals(field.Identifier, routeField, StringComparison.Ordinal))
            .Select(field => field.Identifier)
            .Take(1)
            .ToArray();
        var expectedCommands = new List<PhysicalDocumentQueryCommandKind>();
        if (plan.RequiresPrimaryLookup)
            expectedCommands.Add(PhysicalDocumentQueryCommandKind.LinkedIdentityCollisionCheck);
        expectedCommands.Add(PhysicalDocumentQueryCommandKind.Count);
        if (query.ResultOperation == BoundedQueryResultOperation.Documents)
            expectedCommands.Add(PhysicalDocumentQueryCommandKind.Page);

        return new GroundworkNativeRoutePlanRequest(
            query.DocumentKind,
            query.QueryIdentity,
            plan.LookupObject.Identifier,
            routeField,
            projected.Select(field => field.Identifier).Append(routeField).Distinct(StringComparer.Ordinal).ToArray(),
            Scope,
            routeValue,
            query.Take,
            AcceptanceCardinality,
            candidateDocumentId: "native-000000",
            candidateContentJson: CandidateContent(query.DocumentKind),
            candidateSchemaVersion: CandidateSchemaVersion(query.DocumentKind),
            projectedValues: projectedValues,
            requiredPredicateFields: plan.Predicates
                .Where(predicate => !string.Equals(predicate.Field.Identifier, "storage_scope", StringComparison.Ordinal))
                .Select(predicate => predicate.Field.Identifier)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            expectedCommandKinds: expectedCommands,
            indexFields: [routeField],
            evidenceKind: "groundwork-runtime-route-plan",
            // The shared runtime physicalizer's canonical envelope column is portable across the
            // SQLite linked-index targets. This is benchmark seed metadata only; the public query and
            // provider explanation still determine the actual lookup/primary objects.
            contentColumn: "content_json",
            primaryPhysicalName: plan.RequiresPrimaryLookup ? plan.PrimaryObject.Identifier : null,
            // The outbox physicalizer intentionally maps the logical candidate route to a
            // provider-specific linked index, so its compiled manifest name is not the SQLite
            // index identifier. The provider driver still requires and selects the actual used
            // admitted index when this value is omitted.
            expectedIndexName: query.DocumentKind == "postCommitOutbox" ? null : plan.IndexName?.Identifier,
            matchingCardinality: 1,
            varyingProjectedFields: varying);
    }

    private static GroundworkNativeRouteProjectedValue ProjectedValue(
        PhysicalQueryField field,
        DocumentQuery query,
        PhysicalQueryPlan plan)
    {
        var comparison = query.Clauses
            .SelectMany(clause => clause.Comparisons)
            .FirstOrDefault(candidate => plan.Predicates.Any(predicate =>
                string.Equals(predicate.Path, candidate.Path, StringComparison.Ordinal) &&
                string.Equals(predicate.Field.Identifier, field.Identifier, StringComparison.Ordinal)));
        var value = comparison?.Values.FirstOrDefault(candidate => candidate is not null)
                    ?? $"capture-{field.Identifier}";
        return field.ValueKind switch
        {
            IndexValueKind.Boolean => GroundworkNativeRouteProjectedValue.Boolean(field.Identifier, bool.TryParse(value, out var boolean) && boolean),
            IndexValueKind.Number => GroundworkNativeRouteProjectedValue.Int64(
                field.Identifier,
                long.TryParse(value, out var number) ? number : 1),
            IndexValueKind.DateTime => GroundworkNativeRouteProjectedValue.DateTime(
                field.Identifier,
                DateTimeOffset.TryParse(value, out var date) ? date : FixedNow),
            _ => GroundworkNativeRouteProjectedValue.String(field.Identifier, value)
        };
    }

    private static string CandidateContent(string documentKind) => documentKind switch
    {
        "bookmarkState" => JsonSerializer.Serialize(
            new BookmarkState(
                "native-workflow",
                "native-bookmark",
                "native-activity",
                "native-node",
                "resume",
                "capture-stimulus",
                "capture-hash",
                JsonSerializer.SerializeToElement(new { capture = true }),
                new Dictionary<string, string>(),
                FixedNow,
                null),
            JsonOptions),
        "schedulerWorkItem" => JsonSerializer.Serialize(
            new
            {
                collection = "schedulerWorkItem",
                workflowExecutionId = "native-workflow",
                executionScopeId = (string?)null,
                attempt = (object?)null,
                orderKey = "native-order",
                claimOwnerId = (string?)null,
                claimToken = 0,
                claimedAt = (DateTimeOffset?)null,
                visibleAfter = (DateTimeOffset?)null,
                item = new RuntimeSchedulerWorkItem(
                    "native-work-item",
                    "native-workflow",
                    "native-command",
                    WorkflowExecutionCommandKind.RunSchedulerWork,
                    "native-envelope",
                    "native-idempotency",
                    FixedNow,
                    FixedNow)
            },
            JsonOptions),
        "postCommitOutbox" => JsonSerializer.Serialize(
            new
            {
                collection = "postCommitOutbox",
                workflowExecutionId = "native-workflow",
                deliverableAt = FixedNow,
                claimableAt = FixedNow,
                item = new RuntimePostCommitOutboxItem(
                    "native-000000",
                    new RuntimePostCommitIntent(
                        "native-intent",
                        "native-workflow",
                        "native-capture",
                        FixedNow,
                        null,
                        "native-idempotency",
                        null),
                    RuntimePostCommitOutboxStatus.Pending,
                    FixedNow,
                    FixedNow)
            },
            JsonOptions),
        _ => "{}"
    };

    private static string CandidateSchemaVersion(string documentKind) => documentKind switch
    {
        "postCommitOutbox" => "4",
        "schedulerWorkItem" => "3",
        _ => "1"
    };

    private static void WriteRawPlans(
        string path,
        IReadOnlyList<PhysicalDocumentQueryCommandExplanation> commands)
    {
        var raw = string.Join(
            "\n\n",
            commands.Select((command, index) =>
                $"command={index}\nidentity={command.Identity}\nformat={command.NativePlanFormat}\n{command.NativePlan}"));
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("User ID=", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("mongodb://", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            throw new PerformanceContractException(
                $"Provider-native route plan '{Path.GetFileName(path)}' was empty or contained connection material; refusing to stage it.");
        }
        File.WriteAllText(path, raw);
    }

    private static string RawPlanReference(string workloadId, string provider, string routeIdentity) =>
        $"{workloadId}.{provider}.{routeIdentity}.native-plan.txt";

    private static RouteCaptureDefinition[] Definitions(string workloadId) => workloadId switch
    {
        "bookmark-lookup" =>
        [
            new(
                "list-by-stimulus-and-type",
                async (services, token) =>
                {
                    var index = services.GetRequiredService<IBookmarkStateStore>() as IBookmarkStimulusIndex
                                ?? throw new PerformanceContractException(
                                    "The admitted bookmark state store does not expose IBookmarkStimulusIndex.");
                    var page = await index.ListByStimulusPageAsync(
                        new BookmarkStimulusPageQuery("capture-stimulus", "capture-hash", QueryLimit), token);
                    return page.Items.Count;
                }),
            new(
                "list-by-stimulus-type",
                async (services, token) =>
                {
                    var index = services.GetRequiredService<IBookmarkStateStore>() as IBookmarkStimulusIndex
                                ?? throw new PerformanceContractException(
                                    "The admitted bookmark state store does not expose IBookmarkStimulusIndex.");
                    var page = await index.ListByStimulusTypePageAsync(
                        new BookmarkStimulusTypePageQuery("capture-stimulus", QueryLimit), token);
                    return page.Items.Count;
                })
        ],
        "queue-drain" =>
        [
            new(
                "list-pending-scheduler-workflow-executions",
                async (services, token) =>
                {
                    var ids = await services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                        .ListPendingWorkflowExecutionIdsAsync(QueryLimit, token);
                    return ids.Count;
                }),
            new(
                "list-by-workflow-execution",
                async (services, token) =>
                {
                    var page = await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(
                        new RuntimeSchedulerWorkQuery("native-workflow", QueryLimit), token);
                    return page.Items.Count;
                })
        ],
        "outbox-drain" =>
        [
            new(
                "list-claimable",
                async (services, token) =>
                {
                    var claims = await services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>().ClaimAsync(
                        new RuntimePostCommitOutboxClaimRequest("native-capture", FixedNow, TimeSpan.FromMinutes(1), QueryLimit), token);
                    return claims.Count;
                })
        ],
        _ => throw new PerformanceContractException(
            $"Routed native-plan capture has no public runtime route catalog for workload '{workloadId}'.")
    };

    private sealed record RouteCaptureDefinition(
        string Identity,
        Func<IServiceProvider, CancellationToken, Task<int>> Invoke);

    private sealed record RouteObservation(
        RouteCaptureDefinition Definition,
        DocumentQuery Query,
        PhysicalQueryPlan Plan,
        int Materialized);

    private sealed class CapturingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        private readonly IBoundedDocumentStore _inner = inner;

        public DocumentQuery? LastQuery { get; private set; }

        public void Clear() => LastQuery = null;

        public DocumentQuery RequireQuery(string routeIdentity) => LastQuery
            ?? throw new PerformanceContractException(
                $"Public route '{routeIdentity}' did not issue an admitted bounded document query.");

        public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return await _inner.QueryAsync(query, cancellationToken);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return _inner.CountAsync(query, cancellationToken);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return _inner.FirstOrDefaultAsync(query, cancellationToken);
        }

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return _inner.AnyAsync(query, cancellationToken);
        }
    }
}
