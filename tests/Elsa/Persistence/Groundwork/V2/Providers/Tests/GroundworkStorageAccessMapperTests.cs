using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Providers.Tests;

public sealed class GroundworkStorageAccessMapperTests
{
    [Fact]
    public void Scoped_context_maps_to_the_exact_selected_scope()
    {
        var context = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        var access = GroundworkStorageAccessMapper.Map(context, ScopePolicy.Scoped, "elsa-workflows-design");

        Assert.Equal(ScopePolicy.Scoped, access.Policy);
        Assert.Equal("tenant-a", access.Scope?.Value);
        Assert.False(access.IsPrivilegedAcrossScopes);
    }

    [Fact]
    public void Privileged_across_scope_context_preserves_identity_and_purpose()
    {
        var context = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("recover-stalled-workflows"));

        var access = GroundworkStorageAccessMapper.Map(context, ScopePolicy.Scoped, "elsa-recovery");

        Assert.True(access.IsPrivilegedAcrossScopes);
        Assert.Null(access.Scope);
        Assert.Equal("elsa-recovery", access.Audit?.Identity);
        Assert.Equal("recover-stalled-workflows", access.Audit?.Purpose);
    }

    [Fact]
    public void Global_context_maps_only_to_a_global_unit()
    {
        var access = GroundworkStorageAccessMapper.Map(
            PersistenceAccessContext.Global,
            ScopePolicy.Global,
            "elsa-host");

        Assert.Equal(ScopePolicy.Global, access.Policy);
        Assert.Null(access.Scope);
        Assert.False(access.IsPrivilegedAcrossScopes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scope_policy_mismatches_fail_closed(bool scopedUnit)
    {
        var context = scopedUnit
            ? PersistenceAccessContext.Global
            : PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GroundworkStorageAccessMapper.Map(
                context,
                scopedUnit ? ScopePolicy.Scoped : ScopePolicy.Global,
                "elsa-workflows-design"));

        Assert.Contains("scope", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Across_scope_access_is_refused_for_a_global_unit()
    {
        var context = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("recover-stalled-workflows"));

        Assert.Throws<InvalidOperationException>(() =>
            GroundworkStorageAccessMapper.Map(context, ScopePolicy.Global, "elsa-recovery"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Privileged_across_scope_access_requires_an_audit_identity(string? auditIdentity)
    {
        var context = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("recover-stalled-workflows"));

        Assert.ThrowsAny<ArgumentException>(() =>
            GroundworkStorageAccessMapper.Map(context, ScopePolicy.Scoped, auditIdentity!));
    }

    [Fact]
    public void Unknown_unit_scope_policy_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GroundworkStorageAccessMapper.Map(
                PersistenceAccessContext.Global,
                (ScopePolicy)int.MaxValue,
                "elsa-host"));
    }

    [Fact]
    public void Mapped_privileged_access_runs_a_scope_preserving_query_and_remains_query_only()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-access-{Guid.NewGuid():N}.db");
        var unit = StorageUnit.Declare("access_mapper", "access_mapper")
            .String("id", 64, column => column.Required())
            .Key("id")
            .Scoped()
            .Build();
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            connection.Schema.Apply(unit);
            foreach (var scope in new[] { "tenant-a", "tenant-b" })
            {
                connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(scope))).Insert(
                    new StorageValues(new Dictionary<string, object?> { ["id"] = "same" }),
                    WriteOptions.Unconditional);
            }

            var observer = new RecordingStorageAccessObserver();
            var mapped = GroundworkStorageAccessMapper.Map(
                PersistenceAccessContext.PrivilegedAcrossScopes(
                    new PersistenceAccessPurpose("recover-stalled-workflows")),
                unit.Scope,
                "elsa-recovery",
                observer);
            var session = connection.OpenSession(unit, mapped);
            var request = new QueryRequest(
                new TableId(unit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.Keyset(10));

            var result = session.QueryAcrossScopes(request);

            Assert.Equal(["tenant-a", "tenant-b"], result.Rows.Select(row => row.Scope.Value));
            Assert.All(result.Rows, row => Assert.Equal("same", row.Values["id"]));
            Assert.Equal(
                ["query-across-scopes.attempt", "query-across-scopes.success"],
                observer.Events.Select(candidate => candidate.Operation));
            Assert.Throws<InvalidOperationException>(() => session.Read(
                new StorageKey(new Dictionary<string, object?> { ["id"] = "same" })));
        }
        finally
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}.schema.lock" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    private sealed class RecordingStorageAccessObserver : IStorageAccessObserver
    {
        public List<StorageAccessEvent> Events { get; } = [];

        public void Observe(StorageAccessEvent accessEvent) => Events.Add(accessEvent);
    }
}
