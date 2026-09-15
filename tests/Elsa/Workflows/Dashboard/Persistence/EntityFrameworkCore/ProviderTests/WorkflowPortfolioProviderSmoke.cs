using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.ProviderTests;

internal static class WorkflowPortfolioProviderSmoke
{
    private static readonly DateTimeOffset AsOf = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new LosslessStringConverter() }
    };

    public static async Task RunAsync(
        Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.RuntimeBookmarksProviderFixture fixture,
        Func<string, WorkflowsDesignDbContext> createDesign,
        Func<string, BookmarkStateDbContext> createRuntime,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var tenant = $"native-t32-portfolio-{Guid.NewGuid():N}";
        var definitionId = $"definition-{Guid.NewGuid():N}";
        var draftId = $"draft-{Guid.NewGuid():N}";
        var sourceId = $"source-{Guid.NewGuid():N}";
        var expiredDefinitionId = $"expired-{definitionId}";
        var retiredDefinitionId = $"retired-{definitionId}";
        var foreignTenant = $"other-{tenant}";
        var foreignDefinitionId = $"foreign-{definitionId}";

        await using (var runtime = createRuntime(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, runtime.Database.ProviderName);
            await runtime.Database.EnsureCreatedAsync();
        }

        await using (var design = createDesign(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, design.Database.ProviderName);
            // Both contexts are module-owned and can share a database; EF EnsureCreated is a
            // whole-database operation, so add Design tables after Runtime has created its model.
            await design.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            design.Definitions.AddRange(
                Definition(definitionId, tenant),
                Definition(expiredDefinitionId, tenant),
                Definition(retiredDefinitionId, tenant),
                Definition(foreignDefinitionId, foreignTenant));
            design.Drafts.Add(new WorkflowDefinitionDraft
            {
                Id = draftId,
                TenantId = tenant,
                WorkflowDefinitionId = definitionId,
                StateSource = new PayloadSerializer().Serialize(WorkflowDefinitionState.Empty),
                CreatedAt = AsOf.AddDays(-1),
                LastModifiedAt = AsOf.AddDays(-1)
            });
            await design.SaveChangesAsync();

            await using var transaction = await design.Database.BeginTransactionAsync();
            design.Definitions.Add(new WorkflowDefinition
            {
                Id = $"rolled-back-{definitionId}", TenantId = tenant,
                CreatedAt = AsOf, LastModifiedAt = AsOf
            });
            await design.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var runtime = createRuntime(fixture.ConnectionString))
        {
            runtime.WorkflowExecutableSourceReferences.AddRange(
                PublishedReference(tenant, sourceId, definitionId),
                PublishedReference(tenant, $"expired-{sourceId}", expiredDefinitionId,
                    expiresAt: AsOf.AddMinutes(-1)),
                PublishedReference(tenant, $"retired-{sourceId}", retiredDefinitionId,
                    deletedAt: AsOf.AddMinutes(-1)),
                PublishedReference(foreignTenant, $"foreign-{sourceId}", foreignDefinitionId));
            await runtime.SaveChangesAsync();
        }

        await using var reopenedDesign = createDesign(fixture.ConnectionString);
        await using var reopenedRuntime = createRuntime(fixture.ConnectionString);
        var source = new EfWorkflowPortfolioDataSource(reopenedDesign, reopenedRuntime,
            new FixedAccessor(tenant), new PayloadSerializer());
        Assert.Equal(new(3, 1, 1), await source.QueryBaseCountsAsync(tenant, AsOf));
        var drafts = new List<WorkflowDefinitionDraft>();
        await foreach (var draft in source.StreamCurrentDraftsAsync(tenant))
            drafts.Add(draft);
        Assert.Equal(draftId, Assert.Single(drafts).Id);
        var foreignSource = new EfWorkflowPortfolioDataSource(reopenedDesign, reopenedRuntime,
            new FixedAccessor(foreignTenant), new PayloadSerializer());
        Assert.Equal(new(1, 1, 0), await foreignSource.QueryBaseCountsAsync(foreignTenant, AsOf));
    }

    private static WorkflowDefinition Definition(string id, string tenant) => new()
    {
        Id = id, TenantId = tenant, Name = id,
        CreatedAt = AsOf.AddDays(-2), LastModifiedAt = AsOf.AddDays(-2)
    };

    private static WorkflowExecutableSourceReferenceEntity PublishedReference(
        string tenant, string sourceId, string definitionId,
        DateTimeOffset? expiresAt = null, DateTimeOffset? deletedAt = null)
    {
        var reference = new WorkflowExecutableSourceReference(
            sourceId, $"artifact-{sourceId}", "WorkflowDefinitionVersion", $"source-{definitionId}",
            "1", definitionId, $"version-{definitionId}", "1", AsOf.AddDays(-1),
            AsOf.AddDays(-1), WorkflowExecutableReferenceScope.Published, expiresAt, deletedAt)
        { TenantId = tenant };
        return new WorkflowExecutableSourceReferenceEntity
        {
            Id = EfRelationalIdentity.HashLengthFramed(tenant, sourceId),
            SourceReferenceId = EfRelationalIdentity.Encode(sourceId),
            SourceReferenceIdHash = EfRelationalIdentity.Hash(sourceId),
            SourceReferenceIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(sourceId, RuntimeArtifactEfModule.IdentityMaximumLength)),
            ArtifactId = EfRelationalIdentity.Encode(reference.ArtifactId),
            ArtifactIdHash = EfRelationalIdentity.Hash(reference.ArtifactId),
            DefinitionVersionId = EfRelationalIdentity.Encode(reference.DefinitionVersionId),
            DefinitionVersionIdHash = EfRelationalIdentity.Hash(reference.DefinitionVersionId),
            DefinitionId = EfRelationalIdentity.Encode(definitionId),
            DefinitionIdHash = EfRelationalIdentity.Hash(definitionId),
            ScopeKey = EfRelationalIdentity.Encode(tenant),
            ScopeKeyHash = EfRelationalIdentity.Hash(tenant),
            ScopeKeyOrderKey = EfRelationalIdentity.CreateOrdinalTextOrderKey(tenant + "\0"),
            Scope = nameof(WorkflowExecutableReferenceScope.Published),
            IsRetired = deletedAt is not null,
            ExpiresAtUtcTicks = expiresAt?.UtcTicks,
            ContentJson = new JsonObject
            {
                ["collection"] = "workflowExecutableSourceReference",
                ["artifactId"] = EfRelationalIdentity.Encode(reference.ArtifactId),
                ["reference"] = JsonNode.Parse(JsonSerializer.Serialize(reference, RuntimeJsonOptions))
            }.ToJsonString(),
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            Revision = 1,
            IncarnationId = Guid.NewGuid().ToString("N")
        };
    }

    private sealed class FixedAccessor(string tenant) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(tenant));
    }

    private sealed class PayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string data) => JsonSerializer.Deserialize<object>(data, Options)!;
        public object Deserialize(string data, Type type) => JsonSerializer.Deserialize(data, type, Options)!;
        public object Deserialize(JsonElement data) => data.Deserialize<object>(Options)!;
        public T Deserialize<T>(string data) => JsonSerializer.Deserialize<T>(data, Options)!;
        public T Deserialize<T>(JsonElement data) => data.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => Options;
    }

    private sealed class LosslessStringConverter : JsonConverter<string>
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
