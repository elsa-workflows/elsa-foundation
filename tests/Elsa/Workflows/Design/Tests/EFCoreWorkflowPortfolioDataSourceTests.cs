using System.Text.Json;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Providers.EFCore;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed class EFCoreWorkflowPortfolioDataSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakePayloadSerializer PayloadSerializer = new();

    [Fact]
    public async Task ReturnsTheCompletePortfolioFixtureBeyondOnePage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-portfolio-ef-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISystemClock>(new FixedSystemClock());
            services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
            await using var provider = services.BuildServiceProvider();
            var factory = new TestDbContextFactory(provider, $"Data Source={path}");
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.WorkflowDefinitions.AddRange(Enumerable.Range(0, 105).Select(index => Definition(index)));
                db.WorkflowDefinitionDrafts.AddRange(Enumerable.Range(0, 30).Select(index => Draft(index)));
                await db.SaveChangesAsync();
            }

            var references = new InMemoryWorkflowExecutableSourceReferenceStore();
            for (var index = 0; index < 50; index++)
                await references.SaveAsync(Reference(index));
            var source = new EFCoreWorkflowPortfolioDataSource(factory, references, PayloadSerializer);

            var counts = await source.QueryBaseCountsAsync("tenant-a", Now);
            var drafts = new List<WorkflowDefinitionDraft>();
            await foreach (var draft in source.StreamCurrentDraftsAsync("tenant-a"))
                drafts.Add(draft);

            Assert.Equal(new WorkflowPortfolioBaseCounts(105, 50, 30), counts);
            Assert.Equal(30, drafts.Count);
            Assert.All(drafts, draft => Assert.NotNull(draft.State));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static WorkflowDefinition Definition(int index) => new()
    {
        Id = $"definition-{index}", TenantId = "tenant-a", Name = $"Definition {index}",
        CreatedAt = Now, LastModifiedAt = Now
    };

    private static WorkflowDefinitionDraft Draft(int index) => new()
    {
        Id = $"draft-{index}", WorkflowDefinitionId = $"definition-{index}", TenantId = "tenant-a",
        State = WorkflowDefinitionState.Empty,
        StateSource = PayloadSerializer.Serialize(WorkflowDefinitionState.Empty),
        CreatedAt = Now, LastModifiedAt = Now
    };

    private static WorkflowExecutableSourceReference Reference(int index) => new(
        $"reference-{index}", $"artifact-{index}", "WorkflowDefinitionVersion", $"version-{index}", "1",
        $"definition-{index}", $"version-{index}", "1", Now, Now, WorkflowExecutableReferenceScope.Published);

    private sealed class TestDbContextFactory(IServiceProvider services, string connectionString) : IDbContextFactory<WorkflowsDesignDbContext>
    {
        public WorkflowsDesignDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<WorkflowsDesignDbContext>();
            SqliteShellFeatureDefaults.ConfigureProvider(builder, typeof(EFCoreWorkflowPortfolioDataSourceTests).Assembly, connectionString, null);
            return new(builder.Options, services);
        }
        public Task<WorkflowsDesignDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FakePayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }

    private sealed class FixedSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
