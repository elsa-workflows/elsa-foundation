using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardConsumerContextTests
{
    [Fact]
    public async Task Ordinary_context_migrates_its_combined_model_and_preserves_caller_changes_during_durable_CAS()
    {
        var path = Path.Combine(Path.GetTempPath(), "dashboard-consumer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            string id; string revision;
            await using (var provider = Provider(path))
            await using (var scope = provider.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var context = services.GetRequiredService<DashboardConsumerContext>();
                Assert.False(typeof(DashboardResourceDbContext).IsAssignableFrom(context.GetType()));
                Assert.Same(services.GetRequiredService<ConsumerDependency>(), context.Dependency);
                Assert.Null(services.GetService<DashboardResourceDbContext>());
                await context.Database.MigrateAsync();
                Assert.Equal("20261106120000_ConsumerDashboard", Assert.Single(await context.Database.GetAppliedMigrationsAsync()));
                var ownEntity = context.Model.FindEntityType(typeof(ConsumerNote))!;
                Assert.Same(DashboardConsumerContext.NoteComparer, ownEntity.FindProperty(nameof(ConsumerNote.Id))!.GetValueComparer());
                Assert.Null(ownEntity.FindIndex(ownEntity.FindProperty(nameof(ConsumerNote.SchemaVersion))!));
                Assert.True(context.Model.FindEntityType(typeof(DashboardResourceRow))!.FindProperty(nameof(DashboardResourceRow.Revision))!.IsConcurrencyToken);
                context.Notes.Add(new() { Id = "existing", Text = "original" }); await context.SaveChangesAsync();
                var pending = await context.Notes.SingleAsync(); pending.Text = "unsaved caller edit";
                context.Notes.Add(new() { Id = "unsaved-insert", Text = "must remain local" });
                var service = services.GetRequiredService<IDashboardResourceService>();
                var actor = DashboardResourceTests.Actor("consumer-owner");
                var input = DashboardResourceTests.Question("consumer-create");
                var saved = await service.CreateAsync(actor, "question", input);
                Assert.Equal(saved.Id, (await service.CreateAsync(actor, "question", input)).Id);
                var stale = saved.Revision;
                saved = await service.UpdateAsync(actor, "question", saved.Id, saved.Revision, new() { Name = "Consumer saved revision", PreserveDefinition = true });
                Assert.Equal("conflict", (await Assert.ThrowsAsync<DashboardResourceException>(() => service.UpdateAsync(actor, "question", saved.Id, stale,
                    new() { Name = "Stale revision", PreserveDefinition = true }).AsTask())).Code);
                Assert.Equal("unsaved caller edit", pending.Text);
                Assert.Equal(EntityState.Modified, context.Entry(pending).State);
                Assert.Equal(EntityState.Added, context.ChangeTracker.Entries<ConsumerNote>().Single(entry => entry.Entity.Id == "unsaved-insert").State);
                Assert.True(context.ChangeTracker.HasChanges());
                Assert.Equal("Consumer saved revision", (await service.GetAsync(actor, "question", saved.Id)).Name);
                id = saved.Id; revision = saved.Revision;
            }
            await using (var restarted = Provider(path))
            await using (var scope = restarted.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DashboardConsumerContext>();
                var own = Assert.Single(await context.Notes.AsNoTracking().ToListAsync());
                Assert.Equal("original", own.Text);
                var service = scope.ServiceProvider.GetRequiredService<IDashboardResourceService>();
                var actor = DashboardResourceTests.Actor("consumer-owner");
                var saved = await service.GetAsync(actor, "question", id);
                Assert.Equal(revision, saved.Revision); Assert.Equal("Consumer saved revision", saved.Name);
                Assert.NotNull(saved.Definition); Assert.True((await service.HistoryAsync(actor, "question", id)).Items.Count >= 2);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ordinary_context_refuses_future_schema_for_read_and_write_without_touching_unrelated_work()
    {
        var path = Path.Combine(Path.GetTempPath(), "dashboard-consumer-schema-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var provider = Provider(path);
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DashboardConsumerContext>();
            await context.Database.MigrateAsync();
            var service = scope.ServiceProvider.GetRequiredService<IDashboardResourceService>();
            var actor = DashboardResourceTests.Actor("consumer-owner");
            var question = await service.CreateAsync(actor, "question", DashboardResourceTests.Question("schema"));
            var store = scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>();
            var original = (await store.FindAsync(actor.TenantId, "question", question.Id))!;
            await context.Set<DashboardResourceRow>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SchemaVersion, "9.0.0"));
            context.Notes.Add(new() { Id = "pending", Text = "still local" });
            Assert.Equal("schemaIncompatible", (await Assert.ThrowsAsync<DashboardResourceException>(() => store.FindAsync(actor.TenantId, "question", question.Id).AsTask())).Code);
            Assert.Equal("schemaIncompatible", (await Assert.ThrowsAsync<DashboardResourceException>(() => store.WriteAsync(new(original with { Revision = "new-revision" }, original.Revision)).AsTask())).Code);
            Assert.Equal(EntityState.Added, Assert.Single(context.ChangeTracker.Entries<ConsumerNote>()).State);
            var unchanged = await context.Set<DashboardResourceRow>().AsNoTracking().SingleAsync();
            Assert.Equal(original.Revision, unchanged.Revision); Assert.Equal("9.0.0", unchanged.SchemaVersion);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_model_or_provider_refuses_the_store_instead_of_falling_back_to_transient_storage(bool providerMissing)
    {
        var services = new ServiceCollection();
        if (providerMissing)
        {
            services.AddDbContext<UnconfiguredConsumerContext>();
            services.AddDashboardResourcesEntityFrameworkCore<UnconfiguredConsumerContext>();
        }
        else
        {
            services.AddDbContext<MissingDashboardModelContext>(options => options.UseSqlite("Data Source=:memory:"));
            services.AddDashboardResourcesEntityFrameworkCore<MissingDashboardModelContext>();
        }
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>();
        Assert.Equal("unavailable", (await Assert.ThrowsAsync<DashboardResourceException>(() => store.FindAsync("tenant", "question", "missing").AsTask())).Code);
    }

    [Fact]
    public void Unsupported_model_provider_is_rejected_before_any_mapping_is_installed()
    {
        var builder = new ModelBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddDashboardResourcesModel("Microsoft.EntityFrameworkCore.InMemory"));
        Assert.Empty(builder.Model.GetEntityTypes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Borrowed_connection_or_ambient_transaction_is_refused_without_enlisting_or_discarding_caller_work(bool ambient)
    {
        var path = Path.Combine(Path.GetTempPath(), "dashboard-consumer-isolation-" + Guid.NewGuid().ToString("N") + ".db");
        await using var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False");
        try
        {
            if (!ambient) await connection.OpenAsync();
            await using var provider = Provider(path, ambient ? null : connection);
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DashboardConsumerContext>();
            await context.Database.MigrateAsync();
            context.Notes.Add(new() { Id = "pending", Text = "caller-owned" });
            var store = scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>();
            using (var transaction = ambient ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null)
                Assert.Equal("unavailable", (await Assert.ThrowsAsync<DashboardResourceException>(() => store.FindAsync("tenant", "question", "missing").AsTask())).Code);
            Assert.Equal(EntityState.Added, Assert.Single(context.ChangeTracker.Entries<ConsumerNote>()).State);
            Assert.Empty(await context.Notes.AsNoTracking().ToArrayAsync());
            if (!ambient) Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        }
        finally { await connection.DisposeAsync(); File.Delete(path); }
    }

    private static ServiceProvider Provider(string path, SqliteConnection? borrowed = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ConsumerDependency>();
        services.AddDbContext<DashboardConsumerContext>(options => {
            if (borrowed is null) options.UseSqlite("Data Source=" + path + ";Pooling=False");
            else options.UseSqlite(borrowed);
        });
        services.AddDashboardResourcesEntityFrameworkCore<DashboardConsumerContext>();
        services.AddSingleton<IDashboardDefinitionValidator, ResourceValidator>();
        services.AddSingleton<IDashboardAudienceDirectory, ResourceDirectory>();
        services.AddDashboardResources();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

public sealed class ConsumerDependency;
public sealed class ConsumerNote
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string SchemaVersion { get; set; } = "consumer-owned";
}

public sealed class DashboardConsumerContext(DbContextOptions<DashboardConsumerContext> options, ConsumerDependency dependency) : DbContext(options)
{
    internal static readonly ValueComparer<string> NoteComparer = new((left, right) => left == right, value => value.GetHashCode(), value => value);
    public ConsumerDependency Dependency => dependency;
    public DbSet<ConsumerNote> Notes => Set<ConsumerNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.MapSchemaFinalization(DashboardResourcesEfModule.HistoryModuleName);
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(entity => EfSchemaFinalization.Maps(entity.ClrType)))
            entity.AddIndex(entity.FindProperty(EfSchemaVersion.ColumnName)!);
        // The host's unrelated stamp follows its own convention. Installing Dashboard mappings
        // after this entity must preserve its comparer and leave its indexes alone.
        OwnModel(modelBuilder);
        modelBuilder.AddDashboardResourcesModel(Database.ProviderName!);
    }
    internal static void OwnModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsumerNote>(entity => {
            entity.ToTable("consumer_notes"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).Metadata.SetValueComparer(NoteComparer);
            entity.Property(row => row.Text).IsRequired(); entity.Property(row => row.SchemaVersion).IsRequired();
        });
    }
}

public sealed class MissingDashboardModelContext(DbContextOptions<MissingDashboardModelContext> options) : DbContext(options);
public sealed class UnconfiguredConsumerContext(DbContextOptions<UnconfiguredConsumerContext> options) : DbContext(options);

// This consumer owns a combined migration: the module's retained baseline plus its own table.
// An application can instead scaffold the combined installed model with ordinary EF tooling.
[DbContext(typeof(DashboardConsumerContext))]
[Migration("20261106120000_ConsumerDashboard")]
public sealed class ConsumerDashboardMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var operation in new Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore.Migrations.DashboardResources.Sqlite.Initial { ActiveProvider = ActiveProvider ?? EfProviderNames.Sqlite }.UpOperations)
            migrationBuilder.Operations.Add(operation);
        migrationBuilder.CreateTable("consumer_notes", table => new {
            Id = table.Column<string>(type: "TEXT", nullable: false), Text = table.Column<string>(type: "TEXT", nullable: false), SchemaVersion = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_consumer_notes", row => row.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("consumer_notes");
        foreach (var operation in new Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore.Migrations.DashboardResources.Sqlite.Initial { ActiveProvider = ActiveProvider ?? EfProviderNames.Sqlite }.DownOperations)
            migrationBuilder.Operations.Add(operation);
    }
}
