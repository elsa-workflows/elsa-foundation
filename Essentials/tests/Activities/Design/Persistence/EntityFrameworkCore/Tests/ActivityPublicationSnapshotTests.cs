using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivityPublicationSnapshotTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Copied_publication_snapshot_refuses_invalid_Unicode_without_repair(bool invalidIdentity)
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var exception = Assert.Throws<ActivityPublicationSnapshotException>(() => new ActivityPublicationSnapshot(
            invalidIdentity ? "\uD800" : "definition", "version", "1.0.0", "activity", ActivityDefinitionVersionResolutionKind.AuthorableActivity,
            "template", "hash", "source", "fingerprint", ActivityDefinitionVersionLifecycle.Active, null, payload,
            invalidIdentity ? payload : new byte[] { 0xFF }, new()));
        Assert.Equal("activity_publication_snapshot_unicode_invalid", exception.Code);
    }

    [Fact]
    public async Task Publication_snapshot_copies_original_JSON_and_distinguishes_known_global_owner_from_acquisition_partition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.ActivityDefinitionVersionPublications.Add(Publication()); await context.SaveChangesAsync();
        const string contract = " {\"contractSchemaVersion\":\"1\", \"inputs\":[],\"outputs\":[],\"outcomes\":[],\"future\": { \"λ\" : [ 1, 2 ] }} ";
        const string provider = "{ \"providerKey\":\"fixture\",\"schemaVersion\":\"1\",\"payload\": { \"alias\" : \"opaque:/λ\" }, \"future\":true }";
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_version_publications SET Contract={contract}, Provider={provider}");
        const string requirements = " [ { \"consumerKey\": \"fixture.consumer\", \"schemaVersion\": \"1\", \"future\": true } ] ";
        const string measurements = " [ { \"key\": \"opaque:/λ\", \"future\": [ 2, 3 ] } ] ";
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_version_publications SET RuntimeRequirements={requirements}, ResourceMeasurements={measurements}");
        string publishedAt;
        await using (var command = connection.CreateCommand()) { command.CommandText = "SELECT PublishedAt FROM elsa_activity_version_publications"; publishedAt = (string)(await command.ExecuteScalarAsync())!; }
        var reader = (IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor());
        var snapshot = await reader.FindAsync("activity-version", "partition", new()); Assert.NotNull(snapshot); Assert.Null(snapshot.TenantId);
        Assert.Equal(contract, Encoding.UTF8.GetString(snapshot.GetContractBytes())); Assert.Equal(provider, Encoding.UTF8.GetString(snapshot.GetProviderBytes()));
        var copy = snapshot.GetContractBytes(); copy[0] ^= 1; Assert.Equal(contract, Encoding.UTF8.GetString(snapshot.GetContractBytes()));
        var details = Assert.IsType<ActivityPublicationSnapshotDetails>(snapshot.Details);
        Assert.Equal("publication", details.PublicationId); Assert.Equal("source-version", details.SourceVersionId);
        Assert.Equal(7, details.DirectDependencyCount); Assert.Equal(11, details.ClosedTemplateCount); Assert.Equal(3, details.ResumeTargetCount);
        Assert.Equal(publishedAt, details.PublishedAt); Assert.Equal("source-draft", snapshot.SourceDraftId);
        Assert.Equal(requirements, Encoding.UTF8.GetString(details.GetRuntimeRequirementsBytes()));
        Assert.Equal(measurements, Encoding.UTF8.GetString(details.GetResourceMeasurementsBytes()));
        copy = details.GetRuntimeRequirementsBytes(); copy[0] ^= 1;
        Assert.Equal(requirements, Encoding.UTF8.GetString(details.GetRuntimeRequirementsBytes()));
        copy = details.GetResourceMeasurementsBytes(); copy[0] ^= 1;
        Assert.Equal(measurements, Encoding.UTF8.GetString(details.GetResourceMeasurementsBytes()));
    }

    [Fact]
    public async Task Publication_payload_type_and_UTF8_byte_bounds_precede_typed_contract_hydration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.ActivityDefinitionVersionPublications.Add(Publication()); await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_version_publications SET Contract={new string('x', 8192)}");
        var reader = (IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor());
        Assert.Equal("activity_publication_snapshot_byte_limit", (await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => reader.FindAsync("activity-version", "partition", new() { MaxJsonBytes = 4096 }).AsTask())).Code);
        await context.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_version_publications SET Contract=X'7B7D'");
        Assert.Equal("activity_publication_snapshot_byte_limit", (await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => reader.FindAsync("activity-version", "partition", new()).AsTask())).Code);
    }

    [Fact]
    public async Task Snapshot_qualifies_the_actual_acquisition_partition_before_returning_a_global_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); await connection.OpenAsync();
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.ActivityDefinitionVersionPublications.Add(Publication()); await context.SaveChangesAsync();
        var reader = (IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor());
        var error = await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => reader.FindAsync("activity-version", "other", new()).AsTask());
        Assert.Equal("activity_publication_snapshot_scope_mismatch", error.Code);
        await using var transaction = await context.Database.BeginTransactionAsync();
        error = await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => reader.FindAsync("activity-version", "partition", new()).AsTask());
        Assert.True(error.Unsupported); Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encoded_storage_frames_are_refused_instead_of_exported_as_original_publication_JSON(bool providerColumn)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); await connection.OpenAsync();
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.ActivityDefinitionVersionPublications.Add(Publication()); await context.SaveChangesAsync();
        var frame = EfPayloadCodec.Encode("{}", EfPayloadCompression.GZip, 0)!;
        if (providerColumn) await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_version_publications SET Provider={frame}");
        else await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_version_publications SET Contract={frame}");
        var error = await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => ((IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor()))
            .FindAsync("activity-version", "partition", new()).AsTask());
        Assert.Equal("activity_publication_snapshot_payload_frame_unsupported", error.Code); Assert.True(error.Unsupported);
    }

    [Fact]
    public async Task UTF16_database_encoding_is_an_explicitly_unsupported_source_profile()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); await connection.OpenAsync();
        await using (var command = connection.CreateCommand()) { command.CommandText = "PRAGMA encoding='UTF-16le'"; await command.ExecuteNonQueryAsync(); }
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var error = await Assert.ThrowsAsync<ActivityPublicationSnapshotException>(() => ((IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor()))
            .FindAsync("absent", "partition", new()).AsTask());
        Assert.Equal("activity_publication_snapshot_encoding_unsupported", error.Code); Assert.True(error.Unsupported);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("partition", true)]
    [InlineData("foreign", false)]
    public async Task Actual_owner_tenant_visibility_retains_global_and_partition_rows_without_foreign_disclosure(string? tenant, bool visible)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); await connection.OpenAsync();
        await using var context = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); var publication = Publication(); publication.TenantId = tenant;
        context.ActivityDefinitionVersionPublications.Add(publication); await context.SaveChangesAsync();
        var actual = await ((IActivityPublicationSnapshotReader)new EfActivityDesignStores(context, new Accessor())).FindAsync("activity-version", "partition", new());
        if (visible) { Assert.NotNull(actual); Assert.Equal(tenant, actual.TenantId); }
        else Assert.Null(actual);
    }

    private static ActivityDefinitionVersionPublication Publication() => new()
    {
        Id = "publication", DefinitionId = "activity-definition", DefinitionVersionId = "activity-version", Version = "1.0.0", ActivityTypeKey = "fixture.Activity",
        ResolutionKind = ActivityDefinitionVersionResolutionKind.AuthorableActivity, Contract = new("1", [], [], []),
        Provider = new("fixture", "1", JsonSerializer.SerializeToElement(new { })), TemplateId = "template", TemplateHash = "hash", SourceReferenceId = "source",
        ProviderFingerprint = "fingerprint", SourceDraftId = "source-draft", SourceVersionId = "source-version",
        DirectDependencyCount = 7, ClosedTemplateCount = 11, ResumeTargetCount = 3, RuntimeRequirements = [], PublishedAt = DateTimeOffset.UnixEpoch
    };
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("partition")); }
}
