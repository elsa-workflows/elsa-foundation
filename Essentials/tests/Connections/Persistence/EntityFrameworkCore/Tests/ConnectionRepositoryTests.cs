using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Tests;

public sealed class ConnectionRepositoryTests
{
    [Fact]
    public async Task A_new_context_reads_committed_metadata_and_keeps_settings_case_insensitive()
    {
        await using var database = await Database.CreateAsync();
        var document = Document("Tenant-A", "Mail");
        document.Definition.Settings["Endpoint"] = "https://example.test";
        document.Definition.SecretFields["token"] = new() { SecretName = "private-reference", SourceKind = "secret" };
        await using (var context = database.Context())
        {
            var store = new EfConnectionRepository(context);
            Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
            Assert.True(await store.TryAddAsync(document));
            Assert.False(await store.TryAddAsync(document));
        }
        document.Definition.Settings["Endpoint"] = "caller-mutated";
        await using var restarted = database.Context();
        var restored = (await new EfConnectionRepository(restarted).FindAsync("Tenant-A", "MAIL"))!;
        Assert.Equal("https://example.test", restored.Definition.Settings["endpoint"]);
        Assert.Equal(0, restored.State.Version);
        Assert.Null(await new EfConnectionRepository(restarted).FindAsync("tenant-a", "mail"));
        Assert.True(await new EfConnectionRepository(restarted).IsSecretReferencedAsync("Tenant-A", "PRIVATE-REFERENCE"));
    }

    [Fact]
    public async Task Exactly_one_independent_writer_advances_a_version_and_its_references()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context()) Assert.True(await new EfConnectionRepository(context).TryAddAsync(Document("tenant", "mail")));
        using var writers = new Barrier(2);
        Task<bool> Write(string secret) => Task.Run(async () =>
        {
            await using var context = database.Context();
            var document = Document("tenant", "mail");
            document.Definition.SecretFields["token"] = new() { SecretName = secret };
            Assert.True(writers.SignalAndWait(TimeSpan.FromSeconds(30)));
            try { await new EfConnectionRepository(context).SaveAsync(document); return true; }
            catch (ConnectionConflictException) { return false; }
        });
        var results = await Task.WhenAll(Write("first"), Write("second"));
        Assert.Single(results, x => x);
        await using var verify = database.Context();
        var store = new EfConnectionRepository(verify);
        var document = (await store.FindAsync("tenant", "mail"))!;
        Assert.Equal(1, document.State.Version);
        var winner = document.Definition.SecretFields["token"].SecretName!;
        Assert.True(await store.IsSecretReferencedAsync("tenant", winner));
        Assert.False(await store.IsSecretReferencedAsync("tenant", winner == "first" ? "second" : "first"));
        Assert.False(await store.DeleteAsync("tenant", "mail", 0));
        Assert.True(await store.DeleteAsync("tenant", "mail", 1));
        Assert.False(await store.IsSecretReferencedAsync("tenant", winner));
        await Assert.ThrowsAsync<ConnectionConflictException>(async () => await store.SaveAsync(document));
    }

    [Fact]
    public async Task Reference_exclusion_and_foreign_tenant_checks_do_not_hide_another_binding()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var store = new EfConnectionRepository(context);
        foreach (var (tenant, name) in new[] { ("tenant", "one"), ("tenant", "two"), ("foreign", "one") })
        {
            var document = Document(tenant, name);
            document.Definition.SecretFields["token"] = new() { SecretName = "shared" };
            Assert.True(await store.TryAddAsync(document));
        }
        Assert.True(await store.IsSecretReferencedAsync("tenant", "shared", "ONE"));
        Assert.True(await store.DeleteAsync("tenant", "two"));
        Assert.False(await store.IsSecretReferencedAsync("tenant", "shared", "ONE"));
        Assert.True(await store.IsSecretReferencedAsync("foreign", "shared"));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=memory-name;Mode=Memory;Cache=Shared")]
    [InlineData("Data Source=file:memory-name?mode=memory&cache=shared")]
    [InlineData("Data Source=file:memory-name?%6dode=memory&cache=shared")]
    [InlineData("Data Source=file:?cache=shared")]
    [InlineData("Data Source=file://localhost?cache=shared")]
    [InlineData("Data Source=file:%3amemory%3a?cache=shared")]
    [InlineData("Data Source=file:custom?vfs=memdb")]
    [InlineData("Data Source=file:%00?cache=shared")]
    public async Task Ephemeral_SQLite_options_do_not_claim_restart_persistence(string connectionString)
    {
        await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>().UseSqlite(connectionString).Options);
        Assert.False((await new EfConnectionRepository(context).GetCapabilitiesAsync()).IsDurable);
    }

    [Theory]
    [InlineData("Data Source=connections.db")]
    [InlineData("Filename=file:connections.db?mode=rwc")]
    [InlineData("Data Source=file:connections.db?mode=rw#mode=memory")]
    public async Task Named_SQLite_files_report_the_persistent_provider_profile(string connectionString)
    {
        await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>().UseSqlite(connectionString).Options);
        var capabilities = await new EfConnectionRepository(context).GetCapabilitiesAsync();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", capabilities.Provider);
        Assert.True(capabilities.IsDurable);
    }

    private static ConnectionDocument Document(string tenant, string name) => new()
    {
        Definition = new() { TenantId = tenant, Name = name, DisplayName = name, TypeName = "http", AuthScheme = "bearer" },
        State = new() { CreatedAt = DateTimeOffset.UtcNow, Version = 0 }
    };

    private sealed class Database(string directory) : IAsyncDisposable
    {
        public ConnectionsSqliteDbContext Context() => new(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(directory, "connections.db") + ";Pooling=False;Default Timeout=30").Options);
        public static async Task<Database> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "elsa-connections-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new Database(directory);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        public ValueTask DisposeAsync() { Directory.Delete(directory, true); return ValueTask.CompletedTask; }
    }
}
