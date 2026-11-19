using System.Data.Common;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Connections.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Tests;

public sealed class ConnectionBoundaryTests
{
    [Theory]
    [InlineData("secret-name")]
    [InlineData("credential-name")]
    [InlineData("source-kind")]
    [InlineData("field-name")]
    [InlineData("field-key")]
    [InlineData("setting-key")]
    [InlineData("type")]
    [InlineData("scheme")]
    [InlineData("display")]
    [InlineData("grant-name")]
    public async Task Malformed_lookup_and_credential_identifiers_are_not_rewritten_into_other_identifiers(string target)
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-lookup-utf16-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await context.Database.EnsureCreatedAsync();
            var repository = new EfConnectionRepository(context);
            var document = Document();
            var field = new ConnectionSecretField { SecretName = "unchanged-reference" };
            document.Definition.SecretFields["token"] = field;
            const string malformed = "lookup-\uD800";
            switch (target)
            {
                case "secret-name": field.SecretName = malformed; break;
                case "credential-name": field.NamedCredentialName = malformed; break;
                case "source-kind": field.SourceKind = malformed; break;
                case "field-name": field.FieldName = malformed; break;
                case "field-key": document.Definition.SecretFields.Clear(); document.Definition.SecretFields[malformed] = field; break;
                case "setting-key": document.Definition.Settings[malformed] = "unchanged-value"; break;
                case "type": document.Definition.TypeName = malformed; break;
                case "scheme": document.Definition.AuthScheme = malformed; break;
                case "display": document.Definition.DisplayName = malformed; break;
                case "grant-name": document.State.GrantSecretName = malformed; break;
            }
            await Assert.ThrowsAnyAsync<ArgumentException>(async () => await repository.TryAddAsync(document));
            Assert.Null(await repository.FindAsync("tenant", "mail"));
            Assert.Empty(await context.SecretReferences.ToListAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Invalid_UTF16_in_a_tenant_cannot_be_replaced_into_another_tenants_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-invalid-tenant-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await context.Database.EnsureCreatedAsync();
            var repository = new EfConnectionRepository(context);
            var original = Document();
            original.Definition.TenantId = "tenant-\uFFFD";
            Assert.True(await repository.TryAddAsync(original));
            var malformed = Document();
            malformed.Definition.TenantId = "tenant-\uD800";
            malformed.Definition.DisplayName = "must not cross the tenant boundary";
            Assert.NotNull(await Record.ExceptionAsync(async () => await repository.SaveAsync(malformed)));
            var restored = (await repository.FindAsync("tenant-\uFFFD", "mail"))!;
            Assert.Equal(0, restored.State.Version);
            Assert.Equal("mail", restored.Definition.DisplayName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_future_reference_schema_refuses_inspection_replacement_and_deletion()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-reference-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await context.Database.EnsureCreatedAsync();
            var repository = new EfConnectionRepository(context);
            var document = Document();
            document.Definition.SecretFields["token"] = new() { SecretName = "future-reference" };
            Assert.True(await repository.TryAddAsync(document));
            await context.SecretReferences.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, "99.0.0"));
            await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.IsSecretReferencedAsync("tenant", "future-reference"));
            await Assert.ThrowsAsync<ConnectionConflictException>(async () => await repository.SaveAsync(document));
            Assert.False(await repository.DeleteAsync("tenant", "mail", 0));
            Assert.False(await repository.DeleteAsync("tenant", "mail"));
            Assert.Equal(0, (await repository.FindAsync("tenant", "mail"))!.State.Version);
            Assert.Equal("99.0.0", (await context.SecretReferences.AsNoTracking().SingleAsync()).SchemaVersion);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumer_transaction_rollback_cannot_restore_an_independently_deleted_connection(bool conditional)
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-delete-owner-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<CustomerContext>(options => options.UseSqlite("Data Source=" + path + ";Pooling=False"));
            services.AddConnectionsRepository<CustomerContext>();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CustomerContext>();
            await context.Database.EnsureCreatedAsync();
            var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            Assert.True(await repository.TryAddAsync(Document()));
            await context.Database.OpenConnectionAsync();
            // A deferred, otherwise idle caller transaction holds no SQLite write lock.
            // Both implementations can delete, so rollback exposes transaction ownership.
            await using var caller = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: true);
            await using (var enlisted = await context.Database.UseTransactionAsync(caller))
            {
                Assert.True(conditional ? await repository.DeleteAsync("tenant", "mail", 0) : await repository.DeleteAsync("tenant", "mail"));
                await enlisted!.RollbackAsync();
            }
            Assert.Null(await repository.FindAsync("tenant", "mail"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Deletion_removes_references_even_when_the_host_disabled_SQLite_foreign_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-delete-refs-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False;Foreign Keys=False").Options);
            await context.Database.EnsureCreatedAsync();
            var repository = new EfConnectionRepository(context);
            var document = Document();
            document.Definition.SecretFields["token"] = new() { SecretName = "reuse-reference" };
            Assert.True(await repository.TryAddAsync(document));
            Assert.True(await repository.DeleteAsync("tenant", "mail", 0));
            Assert.False(await repository.IsSecretReferencedAsync("tenant", "reuse-reference"));
            Assert.Empty(await context.SecretReferences.ToListAsync());
            Assert.True(await repository.TryAddAsync(document));
            Assert.True(await repository.IsSecretReferencedAsync("tenant", "reuse-reference"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task An_unrelated_consumer_context_installs_the_model_without_flushing_its_tracked_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-consumer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<CustomerContext>(options => options.UseSqlite("Data Source=" + path + ";Pooling=False"));
            services.AddConnectionsRepository<CustomerContext>();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<CustomerContext>();
                await context.Database.EnsureCreatedAsync();
                Assert.Same(CustomerContext.CustomerComparer, context.Model.FindEntityType(typeof(CustomerRow))!.FindProperty(nameof(CustomerRow.Id))!.GetValueComparer());
                context.Set<CustomerRow>().Add(new() { Id = "unsaved" });
                var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
                Assert.True(await repository.TryAddAsync(Document()));
                var document = (await repository.FindAsync("tenant", "mail"))!;
                document.Definition.DisplayName = "saved independently";
                await repository.SaveAsync(document);
                Assert.True(context.ChangeTracker.HasChanges());
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<CustomerContext>();
                Assert.Empty(await context.Set<CustomerRow>().ToListAsync());
                var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
                Assert.Equal("saved independently", (await repository.FindAsync("tenant", "mail"))!.Definition.DisplayName);
                Assert.True(await repository.DeleteAsync("tenant", "mail", 1));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task An_older_host_cannot_delete_an_unreadable_document_or_its_references()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await context.Database.EnsureCreatedAsync();
            var repository = new EfConnectionRepository(context);
            var document = Document();
            document.Definition.SecretFields["token"] = new() { SecretName = "keep-reference" };
            Assert.True(await repository.TryAddAsync(document));
            await context.Connections.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, "99.0.0"));
            await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.FindAsync("tenant", "mail"));
            await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.IsSecretReferencedAsync("tenant", "keep-reference"));
            Assert.False(await repository.DeleteAsync("tenant", "mail", 0));
            Assert.False(await repository.DeleteAsync("tenant", "mail"));
            Assert.Equal("99.0.0", (await context.Connections.AsNoTracking().SingleAsync()).SchemaVersion);
            Assert.Single(await context.SecretReferences.AsNoTracking().ToListAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Read_failures_keep_the_provider_cause_and_cancellation_remains_cancellation()
    {
        var cause = new SqliteException("controlled reader failure", 1);
        await using var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(new FailedReader(cause)).Options);
        var repository = new EfConnectionRepository(context);
        var find = await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.FindAsync("tenant", "mail"));
        var list = await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.ListAsync("tenant"));
        var references = await Assert.ThrowsAsync<ConnectionStoreException>(async () => await repository.IsSecretReferencedAsync("tenant", "secret"));
        Assert.All(new[] { find, list, references }, failure => Assert.Same(cause, failure.InnerException));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repository.FindAsync("tenant", "mail", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repository.ListAsync("tenant", cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repository.IsSecretReferencedAsync("tenant", "secret", cancellationToken: cancellation.Token));
    }

    private static ConnectionDocument Document() => new()
    {
        Definition = new() { TenantId = "tenant", Name = "mail", DisplayName = "mail", TypeName = "http", AuthScheme = "bearer" },
        State = new() { CreatedAt = DateTimeOffset.UnixEpoch }
    };

    private sealed class FailedReader(Exception cause) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw cause;
        }
    }

    public sealed class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public static readonly ValueComparer<string> CustomerComparer = new(
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            value => value.GetHashCode(StringComparison.OrdinalIgnoreCase), value => value);
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CustomerRow>().HasKey(x => x.Id);
            modelBuilder.Entity<CustomerRow>().Property(x => x.Id).Metadata.SetValueComparer(CustomerComparer);
            modelBuilder.AddConnectionsModel(Database.ProviderName!);
        }
    }

    public sealed class CustomerRow { public string Id { get; set; } = ""; }
}
