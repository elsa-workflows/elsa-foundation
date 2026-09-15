using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowRunHealthDataSourceTests
{
    private static readonly DateTimeOffset From = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Queries_tenant_isolated_buckets_and_stable_failure_order_without_loading_all_rows()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open())
        {
            seed.Context.WorkflowRunHealthStates.AddRange(
                Row("tenant-a", "workflow-completed", "definition-a", From.AddMinutes(10), WorkflowExecutionStatus.Completed, 2, 1),
                Row("tenant-a", "workflow-z", "definition-z", From.AddMinutes(20), WorkflowExecutionStatus.Faulted, 0, 0),
                Row("tenant-a", "workflow-a2", "definition-a", From.AddHours(1).AddMinutes(20), WorkflowExecutionStatus.Faulted, 1, 1),
                Row("tenant-a", "workflow-running", "definition-running", From.AddHours(10), WorkflowExecutionStatus.Running, 0, 0),
                Row("tenant-b", "workflow-other", "definition-other", From.AddMinutes(15), WorkflowExecutionStatus.Completed, 0, 0));
            await seed.Context.SaveChangesAsync();
        }

        await using var fixture = database.Open();
        var source = new EfWorkflowRunHealthDataSource(fixture.Context, new FixedAccessor("tenant-a"));
        var query = new WorkflowRunHealthQuery(From, From.AddHours(3), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a");
        var result = await source.QueryAsync(new WorkflowRunHealthDataQuery(query, HourlyBuckets(query)), CancellationToken.None);

        Assert.Equal(3, result.StartedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(2, result.IncidentBearingRunCount);
        Assert.Equal(3, result.IncidentCount);
        Assert.Equal(1, result.RunningCount);
        Assert.Equal(["definition-a", "definition-z"], result.HighestFailureDefinitions.Select(x => x.DefinitionId).ToArray());
        Assert.Equal([2, 1, 0], result.Buckets.Select(x => x.StartedCount).ToArray());
        Assert.Equal([1, 1, 0], result.Buckets.Select(x => x.IncidentBearingRunCount).ToArray());
        Assert.Equal([2, 1, 0], result.Buckets.Select(x => x.IncidentCount).ToArray());
    }

    [Fact]
    public async Task Include_test_runs_includes_test_projection_without_cross_tenant_disclosure()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open())
        {
            seed.Context.WorkflowRunHealthStates.AddRange(
                Row("tenant-a", "workflow-published", "definition-published", From.AddMinutes(5), WorkflowExecutionStatus.Completed, 0, 0, WorkflowRunKind.PublishedRun),
                Row("tenant-a", "workflow-test", "definition-test", From.AddMinutes(15), WorkflowExecutionStatus.Completed, 0, 0, WorkflowRunKind.TestRun),
                Row("tenant-b", "workflow-other", "definition-other", From.AddMinutes(25), WorkflowExecutionStatus.Faulted, 0, 0));
            await seed.Context.SaveChangesAsync();
        }

        await using var fixture = database.Open();
        var source = new EfWorkflowRunHealthDataSource(fixture.Context, new FixedAccessor("tenant-a"));
        var withoutTests = new WorkflowRunHealthQuery(From, From.AddHours(1), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a");
        var withTests = withoutTests with { IncludeTestRuns = true };

        var excluded = await source.QueryAsync(new(withoutTests, Buckets(withoutTests)));
        var included = await source.QueryAsync(new(withTests, Buckets(withTests)));

        Assert.Equal(1, excluded.StartedCount);
        Assert.Equal(2, included.StartedCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.QueryAsync(new(
            withoutTests with { TenantId = "tenant-b" }, Buckets(withoutTests with { TenantId = "tenant-b" })) ).AsTask());
    }

    [Fact]
    public async Task Corrupt_physical_or_authoritative_projection_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open())
        {
            seed.Context.WorkflowRunHealthStates.Add(Row("tenant-a", "workflow-a", "definition-a", From.AddMinutes(5), WorkflowExecutionStatus.Completed, 0, 0));
            await seed.Context.SaveChangesAsync();
        }

        await using (var corrupt = database.Open())
        {
            var row = await corrupt.Context.WorkflowRunHealthStates.SingleAsync();
            row.DefinitionId = EfRelationalIdentity.Encode("definition-corrupt");
            await corrupt.Context.SaveChangesAsync();
        }

        await using var fixture = database.Open();
        var source = new EfWorkflowRunHealthDataSource(fixture.Context, new FixedAccessor("tenant-a"));
        var query = new WorkflowRunHealthQuery(From, From.AddHours(1), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => source.QueryAsync(new(query, Buckets(query))).AsTask());
    }

    [Fact]
    public async Task Rejects_excessive_bucket_fanout_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open();
        var source = new EfWorkflowRunHealthDataSource(fixture.Context, new FixedAccessor("tenant-a"));
        var query = new WorkflowRunHealthQuery(From, From.AddHours(745), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a");
        var buckets = Enumerable.Range(0, 745)
            .Select(index => new WorkflowRunHealthBucketRange(index, From.AddHours(index), From.AddHours(index + 1)))
            .ToArray();

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(() => source.QueryAsync(new(query, buckets)).AsTask());
    }

    [Fact]
    public void Registration_requires_runtime_ef_context_and_replaces_source()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore("tenant-a");
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowRunHealthEntityFrameworkCore());
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        services.AddWorkflowRunHealthEntityFrameworkCore();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<EfWorkflowRunHealthDataSource>(provider.GetRequiredService<IWorkflowRunHealthDataSource>());
    }

    private static IReadOnlyCollection<WorkflowRunHealthBucketRange> Buckets(WorkflowRunHealthQuery query) =>
        [new(0, query.From, query.To)];

    private static IReadOnlyCollection<WorkflowRunHealthBucketRange> HourlyBuckets(WorkflowRunHealthQuery query) =>
        Enumerable.Range(0, checked((int)(query.To - query.From).TotalHours))
            .Select(index => new WorkflowRunHealthBucketRange(index, query.From.AddHours(index), query.From.AddHours(index + 1)))
            .ToArray();

    private static WorkflowRunHealthStateEntity Row(
        string scope,
        string workflowExecutionId,
        string definitionId,
        DateTimeOffset? startedAt,
        WorkflowExecutionStatus status,
        long incidentCount,
        long incidentBearingCount,
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun)
    {
        var row = new WorkflowRunHealthStateEntity
        {
            Id = CompositeId(scope, workflowExecutionId),
            ScopeKey = EfRelationalIdentity.Encode(scope),
            ScopeKeyHash = EfRelationalIdentity.Hash(scope),
            WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
            WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
            WorkflowExecutionIdOrderKey = Order(workflowExecutionId),
            DefinitionId = EfRelationalIdentity.Encode(definitionId),
            DefinitionIdHash = EfRelationalIdentity.Hash(definitionId),
            DefinitionIdOrderKey = Order(definitionId),
            RunKind = (int)runKind,
            StartedAtUtcTicks = startedAt?.UtcTicks,
            StartedAtOffsetMinutes = startedAt is { } value ? (int)value.Offset.TotalMinutes : null,
            Status = (int)status,
            IncidentCount = incidentCount,
            IncidentBearingCount = incidentBearingCount,
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = 1
        };
        row.ContentJson = JsonSerializer.Serialize(
            new RawProjection(
                row.WorkflowExecutionId,
                row.DefinitionId,
                runKind,
                startedAt,
                status,
                incidentCount,
                incidentBearingCount),
            JsonOptions);
        return row;
    }

    private static string CompositeId(string scope, string workflowExecutionId) =>
        EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}");

    private static string Order(string value) => Convert.ToHexString(
        EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private sealed record RawProjection(
        string WorkflowExecutionId,
        string DefinitionId,
        WorkflowRunKind RunKind,
        DateTimeOffset? StartedAt,
        WorkflowExecutionStatus Status,
        long IncidentCount,
        long IncidentBearingCount);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r25-health-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new(keeper, connectionString);
        }

        public Fixture Open() => new(
            new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connectionString).Options));

        public async ValueTask DisposeAsync() => await keeper.DisposeAsync();
    }

    private sealed class Fixture(BookmarkStateSqliteDbContext context) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
