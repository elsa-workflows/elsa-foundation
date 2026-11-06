using System.Globalization;
using System.Text.RegularExpressions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Physical projection controls only; no family adapter, source authority or claim support is supplied.</summary>
public sealed class RuntimeDeliveryDiscoveryProjectionTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("scope")] [InlineData("tenant")] [InlineData("run")] [InlineData("partition")]
    [InlineData("system")] [InlineData("root")] [InlineData("metadata-key")] [InlineData("metadata-value")]
    public void Every_exact_production_context_dimension_changes_full_projection(string dimension)
    {
        var first = Production();
        var other = Production(scope: dimension == "scope" ? "other" : "store", tenant: dimension == "tenant" ? "tenant" : null,
            kind: dimension == "run" ? WorkflowRunKind.Unknown : WorkflowRunKind.PublishedRun,
            partition: dimension == "partition" ? new("partition") : null,
            authority: new(dimension == "system" ? "other" : "system", dimension == "root" ? "other" : "root",
                new Dictionary<string, string> { [dimension == "metadata-key" ? "other" : "key"] = dimension == "metadata-value" ? "other" : "value" }));
        Assert.False(first.Matches(other));
        Assert.NotEqual(Project(first).Identity, Project(other).Identity);
    }

    [Fact]
    public void Equivalent_metadata_order_and_expiry_offsets_share_projection_and_preserve_existing_authority_recipe()
    {
        var one = new WorkflowExecutionAuthoritySnapshot("system", "root", new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });
        var two = new WorkflowExecutionAuthoritySnapshot("system", "root", new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" });
        var first = Test(authority: one);
        var second = Test(authority: two, expiry: Expiry.ToOffset(TimeSpan.FromHours(2)));
        Assert.True(first.Matches(second)); Assert.Equal(Project(first).Identity, Project(second).Identity);
        Assert.Equal(WorkflowExecutionAuthoritySnapshot.PartitionKey(one.SystemIdentity, one.RootInitiator, one.Metadata), first.AuthorityPartitionKey);
    }

    [Theory]
    [InlineData("id")] [InlineData("expiry")]
    public void Full_test_context_with_equal_coarse_scope_ID_still_has_exact_identity(string dimension)
    {
        var first = Test(); var other = Test(id: dimension == "id" ? "other" : "test", expiry: dimension == "expiry" ? Expiry.AddTicks(1) : Expiry);
        Assert.False(first.Matches(other)); Assert.NotEqual(Project(first).Identity, Project(other).Identity);
    }

    [Fact]
    public void Lossless_framing_distinguishes_real_legacy_authority_hash_aliases_boundaries_NUL_and_case()
    {
        var first = Production(authority: new("\ud800", "root"));
        var second = Production(authority: new("\ud801", "root"));
        Assert.Equal(first.AuthorityPartitionKey, second.AuthorityPartitionKey);
        Assert.NotEqual(Project(first).Identity, Project(second).Identity);
        Assert.NotEqual(Project(first).Hash, Project(second).Hash);
        var firstMetadata = Production(authority: new("system", "root", new Dictionary<string, string> { ["key"] = "\ud800" }));
        var secondMetadata = Production(authority: new("system", "root", new Dictionary<string, string> { ["key"] = "\ud801" }));
        Assert.Equal(firstMetadata.AuthorityPartitionKey, secondMetadata.AuthorityPartitionKey);
        Assert.NotEqual(Project(firstMetadata).Identity, Project(secondMetadata).Identity);
        Assert.NotEqual(Project(Production(authority: new("ab", "c"))).Identity, Project(Production(authority: new("a", "bc"))).Identity);
        Assert.NotEqual(Project(Production(authority: new("a\0b", "c"))).Identity, Project(Production(authority: new("ab", "c"))).Identity);
        Assert.NotEqual(Project(Production(tenant: "Tenant")).Identity, Project(Production(tenant: "tenant")).Identity);
        Assert.NotEqual(Project(Production()).Identity, Project(Production(tenant: "null")).Identity);
        Assert.Matches(new Regex("\\A[0-9A-F]+\\z"), Project(first).Identity);
        Assert.DoesNotContain("root", Project(first).ToString());
    }

    [Fact]
    public void Large_opaque_metadata_is_not_truncated_and_framing_is_culture_invariant()
    {
        var partition = Production(authority: new("system", "root", new Dictionary<string, string> { ["key"] = new('x', 10000) }));
        var expected = Project(partition); var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(expected.Identity, Project(partition).Identity); Assert.Equal(64, expected.Hash.Length);
            Assert.True(expected.Identity.Length > 40000);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Exact_SQL_partition_filter_precedes_Take_even_when_legacy_authority_hashes_alias()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var wanted = Production(authority: new("\ud800", "root"));
        var foreign = Production(authority: new("\ud801", "root"));
        var colliding = Work("coarse-collision", foreign, 0); colliding.FullDiscoveryIdentityHash = Project(wanted).Hash;
        context.RuntimeDeliveryWorks.AddRange(colliding, Work("earlier-foreign", foreign, 1), Work("rightful", wanted, 2));
        await context.SaveChangesAsync();
        var clipped = await context.RuntimeDeliveryWorks.Where(row => row.AuthorityPartitionKey == wanted.AuthorityPartitionKey)
            .OrderBy(row => row.RecordedAtUtcTicks).Take(1).SingleAsync();
        Assert.Equal("coarse-collision", clipped.WorkId);
        var selected = await Project(wanted).FilterWork(context.RuntimeDeliveryWorks.AsNoTracking())
            .OrderBy(row => row.RecordedAtUtcTicks).Take(1).SingleAsync();
        Assert.Equal("rightful", selected.WorkId);
        RuntimeDeliveryDiscoveryProjection.Validate(selected, wanted);
    }

    [Fact]
    public async Task Test_expiry_difference_cannot_clip_a_rightful_page_and_projected_corruption_refuses()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var wanted = Test(); var foreign = Test(expiry: Expiry.AddTicks(1));
        context.RuntimeDeliveryWorks.AddRange(Work("earlier-foreign", foreign, 1), Work("rightful", wanted, 2)); await context.SaveChangesAsync();
        var selected = await Project(wanted).FilterWork(context.RuntimeDeliveryWorks.AsNoTracking()).OrderBy(row => row.RecordedAtUtcTicks).Take(1).SingleAsync();
        Assert.Equal("rightful", selected.WorkId);
        selected.FullDiscoveryIdentityHash = Project(foreign).Hash;
        Assert.Throws<InvalidDataException>(() => RuntimeDeliveryDiscoveryProjection.Validate(selected, wanted));
        selected = Work("rightful", wanted, 2); selected.TenantIdHash = null;
        Assert.Throws<InvalidDataException>(() => RuntimeDeliveryDiscoveryProjection.Validate(selected, wanted));
        var forged = Work("foreign", foreign, 1);
        forged.FullDiscoveryIdentity = Project(wanted).Identity; forged.FullDiscoveryIdentityHash = Project(wanted).Hash;
        Assert.Throws<InvalidDataException>(() => RuntimeDeliveryDiscoveryProjection.Validate(forged, foreign));
        Assert.Equal(0, forged.FencingToken); Assert.Equal(1, forged.Revision);
    }

    [Fact]
    public async Task Source_filter_distinguishes_execution_partition_and_uses_bounded_hash_index_without_indexing_full_text()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var wanted = Production(partition: new("wanted")); var foreign = Production(partition: new("foreign"));
        context.RuntimeIngressSources.AddRange(Source("earlier-foreign", foreign, 1), Source("rightful", wanted, 2)); await context.SaveChangesAsync();
        var selected = await Project(wanted).FilterSources(context.RuntimeIngressSources.AsNoTracking()).OrderBy(row => row.RecordedAtUtcTicks).Take(1).SingleAsync();
        Assert.Equal(EfRelationalIdentity.Encode("rightful"), selected.SourceId);
        RuntimeDeliveryDiscoveryProjection.Validate(selected, wanted);
        var entity = context.Model.FindEntityType(typeof(RuntimeDeliveryWorkEntity))!;
        Assert.Null(entity.FindProperty(nameof(RuntimeDeliveryWorkEntity.FullDiscoveryIdentity))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index => index.Properties.First().Name == nameof(RuntimeDeliveryWorkEntity.FullDiscoveryIdentityHash));
        Assert.DoesNotContain(entity.GetIndexes(), index => index.Properties.Any(property => property.Name == nameof(RuntimeDeliveryWorkEntity.FullDiscoveryIdentity)));
    }

    private static RuntimeDeliveryDiscoveryProjection Project(RuntimeDeliveryDiscoveryPartition partition) => RuntimeDeliveryDiscoveryProjection.Create(partition);
    private static RuntimeDeliveryDiscoveryPartition Production(string scope = "store", string? tenant = null,
        WorkflowRunKind kind = WorkflowRunKind.PublishedRun, WorkflowExecutionPartition? partition = null,
        WorkflowExecutionAuthoritySnapshot? authority = null) => new(new(scope), tenant, kind,
            authority ?? new("system", "root", new Dictionary<string, string> { ["key"] = "value" }), partition);
    private static RuntimeDeliveryDiscoveryPartition Test(string id = "test", DateTimeOffset? expiry = null,
        WorkflowExecutionAuthoritySnapshot? authority = null) => new(new("store"), "tenant", WorkflowRunKind.TestRun,
            authority ?? new("system", "root"), new("partition"), new(id, expiry ?? Expiry, "tenant", new("partition")));
    private static RuntimeDeliveryWorkEntity Work(string id, RuntimeDeliveryDiscoveryPartition partition, long time)
    {
        var projection = Project(partition);
        return new()
        {
            Id = EfRelationalIdentity.HashLengthFramed(partition.Scope.Value, "BusinessMessage", "source", id),
            ScopeKey = EfRelationalIdentity.Encode(partition.Scope.Value), ScopeKeyHash = EfRelationalIdentity.Hash(partition.Scope.Value),
            SourceFamily = EfRelationalIdentity.Encode("BusinessMessage"), SourceFamilyHash = EfRelationalIdentity.Hash("BusinessMessage"),
            SourceId = EfRelationalIdentity.Encode("source"), SourceIdHash = EfRelationalIdentity.Hash("source"),
            TenantId = partition.TenantId is null ? null : EfRelationalIdentity.Encode(partition.TenantId),
            TenantIdHash = partition.TenantId is null ? null : EfRelationalIdentity.Hash(partition.TenantId), RunKind = (int)partition.RunKind,
            TestScopeId = partition.TestScope is null ? null : EfRelationalIdentity.Encode(partition.TestScope.ScopeId),
            TestScopeIdHash = partition.TestScope is null ? null : EfRelationalIdentity.Hash(partition.TestScope.ScopeId),
            AuthorityPartitionKey = partition.AuthorityPartitionKey, FullDiscoveryIdentity = projection.Identity, FullDiscoveryIdentityHash = projection.Hash,
            SourceHeaderRevision = 1, SourceHeaderFingerprint = "header/v1:opaque", WorkId = id, WorkIdHash = EfRelationalIdentity.Hash(id),
            WorkKind = "dispatch", WorkKindHash = EfRelationalIdentity.Hash("dispatch"), Role = 1, RequiredParticipantVersion = 1,
            ContentFingerprint = "work/v1:opaque", RecordedAtUtcTicks = time, ClaimableIsEligible = true, ClaimableAtUtcTicks = time,
            ContentJson = RuntimeArtifactJson.Serialize(partition), SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
        };
    }
    private static RuntimeIngressSourceEntity Source(string id, RuntimeDeliveryDiscoveryPartition partition, long time)
    {
        var values = Work(id, partition, time);
        return new()
        {
            Id = EfRelationalIdentity.HashLengthFramed(partition.Scope.Value, "BusinessMessage", id),
            ScopeKey = values.ScopeKey, ScopeKeyHash = values.ScopeKeyHash, SourceFamily = values.SourceFamily, SourceFamilyHash = values.SourceFamilyHash,
            SourceId = EfRelationalIdentity.Encode(id), SourceIdHash = EfRelationalIdentity.Hash(id), TenantId = values.TenantId, TenantIdHash = values.TenantIdHash,
            RunKind = values.RunKind, TestScopeId = values.TestScopeId, TestScopeIdHash = values.TestScopeIdHash, AuthorityPartitionKey = values.AuthorityPartitionKey,
            FullDiscoveryIdentity = values.FullDiscoveryIdentity, FullDiscoveryIdentityHash = values.FullDiscoveryIdentityHash, HeaderVersion = 1, HeaderRevision = 1,
            RequiredParticipantVersion = 1, HeaderFingerprint = "header/v1:opaque", RecordedAtUtcTicks = time, AcceptanceOperationId = "accept:" + id,
            AcceptanceOperationFingerprint = "acceptance/v1:opaque", OperationProofJson = "{}", ContentJson = values.ContentJson,
            SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
        };
    }
}
