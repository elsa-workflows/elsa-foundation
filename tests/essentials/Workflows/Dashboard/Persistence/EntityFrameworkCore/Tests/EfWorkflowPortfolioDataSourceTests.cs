using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowPortfolioDataSourceTests : IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private static readonly DateTimeOffset AsOf = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TestPayloadSerializer PayloadSerializer = new();
    private TemporarySqliteDatabase designDatabase = null!;
    private TemporarySqliteDatabase runtimeDatabase = null!;
    private WorkflowsDesignSqliteDbContext design = null!;
    private RuntimeSqliteDbContext runtime = null!;

    public async Task InitializeAsync()
    {
        designDatabase = new TemporarySqliteDatabase("dashboard-design");
        runtimeDatabase = new TemporarySqliteDatabase("dashboard-runtime");
        design = new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(designDatabase.ConnectionString).Options);
        runtime = new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(runtimeDatabase.ConnectionString).Options);
        await design.Database.EnsureCreatedAsync();
        await runtime.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await design.DisposeAsync();
        await runtime.DisposeAsync();
        await designDatabase.DisposeAsync();
        await runtimeDatabase.DisposeAsync();
    }

    [Fact]
    public async Task Counts_filter_lifecycle_tenant_and_live_published_sources()
    {
        SeedDefinition("published");
        SeedDefinition("unpublished");
        SeedDefinition("expired");
        SeedDefinition("retired");
        SeedDefinition("deleted", AsOf.AddDays(-1));
        SeedDefinition("other", tenant: "tenant-b");
        SeedDraft("draft-current", "unpublished", AsOf.AddHours(2));
        SeedDraft("draft-old", "unpublished", AsOf.AddHours(1));
        SeedDraft("draft-deleted", "deleted", AsOf.AddHours(1));
        await SeedReferenceAsync("ref-published", "published");
        await SeedReferenceAsync("ref-expired", "expired", expiresAt: AsOf.AddMinutes(-1));
        await SeedReferenceAsync("ref-retired", "retired", deletedAt: AsOf.AddMinutes(-1));
        await SeedReferenceAsync("ref-other", "other", tenant: "tenant-b");

        var counts = await Source().QueryBaseCountsAsync(Tenant, AsOf);

        Assert.Equal(new(4, 1, 1), counts);
    }

    [Fact]
    public async Task Current_drafts_are_streamed_in_stable_definition_and_snapshot_order()
    {
        SeedDefinition("zeta");
        SeedDefinition("alpha");
        SeedDefinition("deleted", AsOf.AddDays(-1));
        SeedDraft("z-old", "zeta", AsOf.AddHours(1));
        SeedDraft("z-current", "zeta", AsOf.AddHours(2));
        SeedDraft("a-current", "alpha", AsOf.AddHours(1));
        SeedDraft("deleted-current", "deleted", AsOf.AddHours(3));

        var drafts = new List<WorkflowDefinitionDraft>();
        await foreach (var draft in Source().StreamCurrentDraftsAsync(Tenant))
            drafts.Add(draft);

        Assert.Equal(["a-current", "z-current"], drafts.Select(x => x.Id).ToArray());
        Assert.All(drafts, draft => Assert.NotNull(draft.State));
    }

    [Fact]
    public async Task Persisted_projections_are_read_after_context_restart()
    {
        SeedDefinition("definition");
        SeedDraft("draft", "definition", AsOf.AddHours(1));
        await SeedReferenceAsync("reference", "definition");

        await design.DisposeAsync();
        await runtime.DisposeAsync();
        design = new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(designDatabase.ConnectionString).Options);
        runtime = new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(runtimeDatabase.ConnectionString).Options);

        var counts = await Source().QueryBaseCountsAsync(Tenant, AsOf);

        Assert.Equal(new(1, 1, 1), counts);
    }

    [Fact]
    public async Task Corrupt_definition_or_authoritative_reference_fails_closed()
    {
        SeedDefinition("definition");
        await SeedReferenceAsync("reference", "definition");
        var row = await runtime.WorkflowExecutableSourceReferences.SingleAsync();
        row.DefinitionIdHash = EfRelationalIdentity.Hash("different-definition");
        await runtime.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => Source().QueryBaseCountsAsync(Tenant, AsOf).AsTask());
    }

    [Fact]
    public async Task Corrupt_design_identity_projection_fails_closed()
    {
        SeedDefinition("definition");
        await design.Database.ExecuteSqlRawAsync(
            "UPDATE elsa_workflow_definitions_v2 SET IdSearchKey = '|000041'");

        await Assert.ThrowsAsync<InvalidDataException>(() => Source().QueryBaseCountsAsync(Tenant, AsOf).AsTask());
    }

    [Fact]
    public async Task Malformed_current_draft_state_fails_closed_when_streamed()
    {
        SeedDefinition("definition");
        SeedDraft("draft", "definition", AsOf.AddHours(1));
        await design.Database.ExecuteSqlRawAsync(
            "UPDATE elsa_workflow_definition_drafts SET StateSource = {0}",
            "{invalid");

        var drafts = Source().StreamCurrentDraftsAsync(Tenant).GetAsyncEnumerator();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await drafts.MoveNextAsync());
        }
        finally
        {
            await drafts.DisposeAsync();
        }
    }

    [Fact]
    public async Task Global_or_cross_scope_access_is_refused()
    {
        SeedDefinition("definition");
        var source = new EfWorkflowPortfolioDataSource(
            design,
            runtime,
            new FixedAccessor(PersistenceAccessContext.Global),
            PayloadSerializer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.QueryBaseCountsAsync(Tenant, AsOf).AsTask());
    }

    [Fact]
    public void Registration_requires_both_ef_owned_lanes()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowPortfolioEntityFrameworkCore());
    }

    [Fact]
    public void Registration_refuses_mixed_runtime_backend()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddPersistenceCore(Tenant);
        services.AddWorkflowsDesignEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });

        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowPortfolioEntityFrameworkCore());
    }

    [Fact]
    public void Registration_refuses_replaced_design_context_without_mutating_services()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddPersistenceCore(Tenant);
        services.AddSingleton<IPayloadSerializer>(PayloadSerializer);
        services.AddWorkflowsDesignEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        var ownedContext = services.Single(descriptor => descriptor.ServiceType == typeof(WorkflowsDesignDbContext));
        services.Remove(ownedContext);
        services.AddScoped<WorkflowsDesignDbContext>(_ => throw new InvalidOperationException("Foreign Design context."));
        var snapshot = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowPortfolioEntityFrameworkCore());
        Assert.Equal(snapshot, services);
    }

    [Fact]
    public void Registration_replaces_the_default_source_when_both_ef_lanes_own_their_contexts()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddPersistenceCore(Tenant);
        services.AddSingleton<IPayloadSerializer>(PayloadSerializer);
        services.AddWorkflowsDesignEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });

        services.AddWorkflowPortfolioEntityFrameworkCore();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowPortfolioDataSource>(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    private EfWorkflowPortfolioDataSource Source() => new(
        design,
        runtime,
        new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(Tenant))),
        PayloadSerializer);

    private void SeedDefinition(string id, DateTimeOffset? deletedAt = null, string tenant = Tenant)
    {
        design.Definitions.Add(new WorkflowDefinition
        {
            Id = id,
            TenantId = tenant,
            Name = id,
            CreatedAt = AsOf.AddDays(-2),
            LastModifiedAt = AsOf.AddDays(-2),
            DeletedAt = deletedAt
        });
        design.SaveChanges();
    }

    private void SeedDraft(string id, string definitionId, DateTimeOffset lastModifiedAt, string tenant = Tenant)
    {
        design.Drafts.Add(new WorkflowDefinitionDraft
        {
            Id = id,
            TenantId = tenant,
            WorkflowDefinitionId = definitionId,
            StateSource = PayloadSerializer.Serialize(WorkflowDefinitionState.Empty),
            CreatedAt = lastModifiedAt.AddMinutes(-1),
            LastModifiedAt = lastModifiedAt
        });
        design.SaveChanges();
    }

    private async Task SeedReferenceAsync(
        string sourceReferenceId,
        string definitionId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? deletedAt = null,
        string tenant = Tenant)
    {
        var scope = tenant;
        var reference = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            $"artifact-{sourceReferenceId}",
            "WorkflowDefinitionVersion",
            $"source-{definitionId}",
            "1",
            definitionId,
            $"version-{definitionId}",
            "1",
            AsOf.AddDays(-1),
            AsOf.AddDays(-1),
            WorkflowExecutableReferenceScope.Published,
            expiresAt,
            deletedAt)
        { TenantId = tenant };
        var encodedId = EfRelationalIdentity.Encode(sourceReferenceId);
        var row = new WorkflowExecutableSourceReferenceEntity
        {
            Id = EfRelationalIdentity.HashLengthFramed(scope, sourceReferenceId),
            SourceReferenceId = encodedId,
            SourceReferenceIdHash = EfRelationalIdentity.Hash(sourceReferenceId),
            SourceReferenceIdOrderKey = Order(sourceReferenceId),
            ArtifactId = EfRelationalIdentity.Encode(reference.ArtifactId),
            ArtifactIdHash = EfRelationalIdentity.Hash(reference.ArtifactId),
            DefinitionVersionId = EfRelationalIdentity.Encode(reference.DefinitionVersionId),
            DefinitionVersionIdHash = EfRelationalIdentity.Hash(reference.DefinitionVersionId),
            DefinitionId = EfRelationalIdentity.Encode(reference.DefinitionId),
            DefinitionIdHash = EfRelationalIdentity.Hash(reference.DefinitionId),
            ScopeKey = EfRelationalIdentity.Encode(scope),
            ScopeKeyHash = EfRelationalIdentity.Hash(scope),
            ScopeKeyOrderKey = EfRelationalIdentity.CreateOrdinalTextOrderKey(scope + "\0"),
            Scope = nameof(WorkflowExecutableReferenceScope.Published),
            IsRetired = deletedAt is not null,
            ExpiresAtUtcTicks = expiresAt?.UtcTicks,
            ContentJson = new JsonObject
            {
                ["collection"] = "workflowExecutableSourceReference",
                ["artifactId"] = rowArtifact(reference.ArtifactId),
                ["reference"] = JsonNode.Parse(JsonSerializer.Serialize(reference, RuntimeJsonOptions))
            }.ToJsonString(),
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            Revision = 1,
            IncarnationId = Guid.NewGuid().ToString("N")
        };
        runtime.WorkflowExecutableSourceReferences.Add(row);
        await runtime.SaveChangesAsync();
    }

    private static string rowArtifact(string value) => EfRelationalIdentity.Encode(value);
    private static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new LosslessUtf16StringConverter() }
    };

    private sealed class FixedAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    private sealed class LosslessUtf16StringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            EfRelationalIdentity.Decode(reader.GetString()!);
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            EfRelationalIdentity.Decode(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EfRelationalIdentity.Encode(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(EfRelationalIdentity.Encode(value));
    }
}
