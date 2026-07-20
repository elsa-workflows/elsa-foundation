using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowRuntimeAttentionQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_scoped_store_reports_an_available_all_clear_snapshot()
    {
        await using var fixture = Fixture();
        var query = Query(fixture);

        var result = await query.QueryAsync(Request());

        Assert.True(result.IsAvailable);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Records);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Bounded_routes_return_active_incidents_and_uncovered_faults_without_cross_scope_records(string provider)
    {
        await using var fixture = Fixture(provider);
        var (executions, incidents, query) = Stores(fixture);
        await executions.SaveAsync(Execution("faulted", "definition-faulted", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-3)));
        await executions.SaveAsync(Execution("blocked", "definition-blocked", WorkflowExecutionStatus.Running, Now.AddMinutes(-2)));
        await executions.SaveAsync(Execution("open", "definition-open", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-1)));
        await incidents.SaveAsync(Incident("incident-blocked", "blocked", IncidentStatus.Blocking, Now.AddMinutes(-2)));
        await incidents.SaveAsync(Incident("incident-open", "open", IncidentStatus.Open, Now.AddMinutes(-1)));
        await incidents.SaveAsync(Incident("incident-orphan", "missing", IncidentStatus.Blocking, Now));
        var foreignExecutions = new GroundworkWorkflowExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.AccessContext("tenant-2"),
            fixture.BoundedDocumentStore);
        await foreignExecutions.SaveAsync(Execution(
            "foreign-fault",
            "definition-foreign",
            WorkflowExecutionStatus.Faulted,
            Now,
            tenantId: "tenant-2"));
        await incidents.SaveAsync(Incident("incident-foreign", "foreign-fault", IncidentStatus.Blocking, Now));

        var result = await query.QueryAsync(Request(maximumItems: 10));

        Assert.Equal(3, result.TotalCount);
        Assert.Collection(
            result.Records,
            record =>
            {
                Assert.Equal("blocked", record.WorkflowExecutionId);
                Assert.Equal("incident-blocked", record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.BlockingIncident, record.Kind);
                Assert.Equal("definition-blocked", record.WorkflowDefinitionId);
            },
            record =>
            {
                Assert.Equal("faulted", record.WorkflowExecutionId);
                Assert.Null(record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.FaultedExecution, record.Kind);
            },
            record =>
            {
                Assert.Equal("open", record.WorkflowExecutionId);
                Assert.Equal("incident-open", record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.OpenIncident, record.Kind);
            });
        Assert.All(result.Records, record => Assert.Null(record.SanitizedSummary));
    }

    [Fact]
    public async Task Attention_pages_only_faulted_executions_and_keeps_exact_totals_across_multiple_pages()
    {
        await using var fixture = Fixture();
        var bounded = new RecordingBoundedDocumentStore(fixture.BoundedDocumentStore);
        var (executions, incidents, query) = Stores(fixture, bounded);

        for (var index = 0; index < 600; index++)
        {
            await executions.SaveAsync(Execution(
                $"healthy-{index:D4}",
                "definition-healthy",
                WorkflowExecutionStatus.Completed,
                Now.AddMinutes(-2000 - index)));
        }

        for (var index = 0; index <= ElsaGroundworkQueryRoutes.MaximumResultCount; index++)
        {
            await executions.SaveAsync(Execution(
                $"fault-{index:D4}",
                "definition-fault",
                WorkflowExecutionStatus.Faulted,
                Now.AddMinutes(-index)));
        }

        await incidents.SaveAsync(Incident(
            "incident-blocking",
            $"fault-{ElsaGroundworkQueryRoutes.MaximumResultCount:D4}",
            IncidentStatus.Blocking,
            Now));

        var result = await query.QueryAsync(Request(maximumItems: 3));

        Assert.Equal(ElsaGroundworkQueryRoutes.MaximumResultCount + 1, result.TotalCount);
        Assert.Collection(
            result.Records,
            record =>
            {
                Assert.Equal("incident-blocking", record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.BlockingIncident, record.Kind);
            },
            record => Assert.Equal("fault-0000", record.WorkflowExecutionId),
            record => Assert.Equal("fault-0001", record.WorkflowExecutionId));

        var executionPages = bounded.Queries
            .Where(documentQuery => documentQuery.QueryIdentity == ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery)
            .ToArray();
        Assert.True(executionPages.Length >= 2);
        Assert.All(executionPages, documentQuery =>
            Assert.Contains(
                documentQuery.Clauses.SelectMany(clause => clause.Comparisons),
                comparison => comparison.Path == ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField &&
                              comparison.Values.SequenceEqual([((int)WorkflowExecutionStatus.Faulted).ToString()])));
        Assert.Equal(
            2,
            bounded.Queries.Count(documentQuery =>
                documentQuery.QueryIdentity == ElsaRuntimeStorageManifest.PageAttentionIncidentsByStatusQuery));
    }

    [Fact]
    public void Groundwork_runtime_registration_replaces_the_attention_fallback()
    {
        var services = new ServiceCollection();
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        services.AddSingleton<IDocumentStore>(documents);
        services.AddSingleton<IBoundedDocumentStore>(documents);
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeAttentionFeature().ConfigureServices(services);
        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IWorkflowRuntimeAttentionQuery>();

        Assert.IsType<GroundworkWorkflowRuntimeAttentionQuery>(query);
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<GroundworkWorkflowExecutionStateStore>(),
            scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<GroundworkIncidentStateStore>(),
            scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IIncidentStateStore>());
    }

    private static GroundworkDocumentStoreFixture Fixture(string provider = "memory") =>
        GroundworkDocumentStoreFixture.Create(provider, ElsaRuntimeStorageManifest.CreatePhysicalized());

    private static GroundworkWorkflowRuntimeAttentionQuery Query(GroundworkDocumentStoreFixture fixture) =>
        Stores(fixture).Query;

    private static (
        GroundworkWorkflowExecutionStateStore Executions,
        GroundworkIncidentStateStore Incidents,
        GroundworkWorkflowRuntimeAttentionQuery Query) Stores(GroundworkDocumentStoreFixture fixture)
        => Stores(fixture, fixture.BoundedDocumentStore);

    private static (
        GroundworkWorkflowExecutionStateStore Executions,
        GroundworkIncidentStateStore Incidents,
        GroundworkWorkflowRuntimeAttentionQuery Query) Stores(
        GroundworkDocumentStoreFixture fixture,
        IBoundedDocumentStore boundedStore)
    {
        var executions = new GroundworkWorkflowExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.AccessContext("tenant-1"),
            boundedStore);
        var incidents = new GroundworkIncidentStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            boundedStore);
        return (executions, incidents, new GroundworkWorkflowRuntimeAttentionQuery(
            executions,
            incidents,
            new FixedTimeProvider(Now)));
    }

    private static WorkflowRuntimeAttentionQuery Request(int maximumItems = 5) =>
        new(new AttentionQueryContext(new ClaimsPrincipal(), "tenant-1"), maximumItems);

    private static WorkflowExecutionState Execution(
        string id,
        string definitionId,
        WorkflowExecutionStatus status,
        DateTimeOffset timestamp,
        string tenantId = "tenant-1") => new(
        id,
        new($"artifact-{id}", definitionId, "version-1", "1.0.0", "hash-1"),
        status,
        null,
        timestamp,
        timestamp,
        timestamp,
        status.IsTerminal() ? timestamp : null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>());

    private static IncidentState Incident(
        string id,
        string workflowExecutionId,
        IncidentStatus status,
        DateTimeOffset createdAt) => new(
        id,
        workflowExecutionId,
        null,
        null,
        IncidentSeverity.Critical,
        status,
        IncidentResolutionAction.WaitForIntervention,
        "TestFailure",
        "Sensitive failure detail",
        createdAt,
        null);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
