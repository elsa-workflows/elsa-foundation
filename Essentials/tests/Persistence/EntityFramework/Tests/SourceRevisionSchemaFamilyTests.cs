using Elsa.Persistence.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class SourceRevisionSchemaFamilyTests
{
    private static readonly SourceScope Scope = new("authoring", "tenant", "agent", "agent-design");

    [Fact]
    public async Task Shared_source_writes_keep_their_own_stamp_when_the_consumer_module_has_another_version()
    {
        await using var file = new TemporarySqliteDatabase();
        await using var db = new MixedContext(file.ConnectionString);
        await db.Database.EnsureCreatedAsync();
        db.Add(new DomainProbe { Id = "domain" });
        await db.SaveChangesAsync();
        var store = new EfSourceRevisions(db, Scope);
        await store.CreateAsync("agent", "draft", "Agent", "agent/1", "content");
        var snapshot = await store.CaptureAsync("agent", "draft", 0, "capture");
        db.ChangeTracker.Clear();
        Assert.Equal("content", (await store.FindAsync("agent", "draft"))!.Content);
        Assert.Equal(snapshot.Key, (await store.FindSnapshotAsync("agent", "draft", snapshot.Key))!.Key);
        Assert.Equal(MixedContext.ModuleVersion, await db.Set<DomainProbe>().Select(x => EF.Property<string>(x, EfSchemaVersion.ColumnName)).SingleAsync());
        Assert.Equal(EfSourceModel.Chain.CurrentVersion, await db.Set<SourceWorkingCopy>().Select(x => EF.Property<string>(x, EfSchemaVersion.ColumnName)).SingleAsync());
        Assert.Equal(EfSourceModel.Chain.CurrentVersion, await db.Set<SourceSnapshot>().Select(x => EF.Property<string>(x, EfSchemaVersion.ColumnName)).SingleAsync());
    }

    [Fact]
    public async Task Shared_source_materialization_checks_its_family_independently_of_the_consumer_module()
    {
        await using var file = new TemporarySqliteDatabase();
        await using (var seed = new PlainContext(file.ConnectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            await new EfSourceRevisions(seed, Scope).CreateAsync("agent", "draft", "Agent", "agent/1", "content");
        }
        await using var db = new MixedContext(file.ConnectionString);
        Assert.Equal("content", (await db.Set<SourceWorkingCopy>().AsNoTracking().SingleAsync()).Content);
        // A stamp admitted by the consumer is still foreign to the shared source family.
        await db.Database.ExecuteSqlRawAsync("UPDATE source_copies SET SchemaVersion = '7.0.0'");
        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => db.Set<SourceWorkingCopy>().AsNoTracking().SingleAsync());
        Assert.Equal(EfSourceModel.Chain.Family, skew.Family);
        Assert.Equal(MixedContext.ModuleVersion, skew.Found);
        Assert.Equal(EfSourceModel.Chain.CurrentVersion, skew.Expected);
    }

    private sealed class DomainProbe { public string Id { get; set; } = ""; }
    private static void Map(ModelBuilder model, string provider)
    {
        model.AddSourceRevisions("source", provider);
        model.Entity<DomainProbe>().ToTable("domain").HasKey(x => x.Id);
        model.Entity<DomainProbe>().Property<string>(EfSchemaVersion.ColumnName).IsRequired();
    }
    private sealed class PlainContext(string connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model) => Map(model, Database.ProviderName!);
    }
    private sealed class MixedContext(string connection) : DbContext, IEfSchemaVersionedContext
    {
        public const string ModuleVersion = "7.0.0";
        EfSchemaChain IEfSchemaVersionedContext.SchemaChain => SchemaChains.Single("ConsumerModule", ModuleVersion);
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite(connection);
            EfSchemaVersionMaterializationInterceptor.EnsureAdded(options);
        }
        protected override void OnModelCreating(ModelBuilder model) => Map(model, Database.ProviderName!);
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EfSchemaVersionMaterializationInterceptor.StampWrites(this);
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}
