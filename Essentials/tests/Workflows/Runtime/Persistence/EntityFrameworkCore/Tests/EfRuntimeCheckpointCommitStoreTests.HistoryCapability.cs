using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly IRuntimeRecoveryContinuationCodec HistoryCapabilityCodec = new HmacRuntimeRecoveryContinuationCodec(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "history-capability-controls-0123456789", AllowEphemeralDevelopmentKey = false }));

    [Fact]
    public void Public_native_history_capability_requires_its_actual_reader_context_accessor_and_declared_chain()
    {
        var services = CompleteHistoryReaderServices(); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        using var provider = services.BuildServiceProvider(); using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var writer = first.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var reader = first.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        Assert.True(writer.SupportsHistory); Assert.True(writer.CanCommitHistory(reader));
        Assert.False(writer.CanCommitHistory(second.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>()));
        var context = first.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var access = first.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
        Assert.False(writer.CanCommitHistory(new EfRuntimeHistoryStore(context, new FixedAccessor(access.Current.RequireScope().Value), HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain)));
        var foreignChain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("foreign-history-owner", "Workflows.Runtime", RuntimeHistoryEfModule.SchemaVersion,
            typeof(EfRuntimeCheckpointCommitStoreTests).Assembly) { ContentColumns = RuntimeHistoryEfModule.Chain.ContentColumns.ToArray() });
        Assert.False(writer.CanCommitHistory(new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, foreignChain)));
        Assert.Null(first.ServiceProvider.GetService<RuntimeHistoryOptions>());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("global")] [InlineData("privileged")] [InlineData("across")]
    public void Native_and_independent_capability_refuse_nonordinary_access_without_executing_data_reads(string boundary)
    {
        using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess { Current = boundary switch
        {
            "global" => PersistenceAccessContext.Global,
            "privileged" => PersistenceAccessContext.PrivilegedScoped(new("tenant-a"), new("maintenance")),
            _ => PersistenceAccessContext.PrivilegedAcrossScopes(new("maintenance"))
        } };
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(writer.CanCommitHistory(reader)); Assert.False(observer.CanRecordHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public void A_partial_history_model_cannot_advertise_native_or_independent_pairing()
    {
        using var context = new IncompleteHistoryCapabilityContext(new DbContextOptionsBuilder<IncompleteHistoryCapabilityContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess();
        Assert.NotNull(context.Model.FindEntityType(typeof(RuntimeHistoryHeadEntity)));
        Assert.Null(context.Model.FindEntityType(typeof(RuntimeHistoryOccurrenceMembershipEntity)));
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(writer.CanCommitHistory(reader)); Assert.False(observer.CanRecordHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void Retaining_content_without_the_occurrence_ordinal_cannot_qualify_history_pairing()
    {
        using var context = new MissingOrdinalCapabilityContext(new DbContextOptionsBuilder<MissingOrdinalCapabilityContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess();
        var membership = context.Model.FindEntityType(typeof(RuntimeHistoryOccurrenceMembershipEntity)); Assert.NotNull(membership);
        Assert.NotNull(membership.FindProperty(nameof(RuntimeHistoryOccurrenceMembershipEntity.ContentJson)));
        Assert.Null(membership.FindProperty(nameof(RuntimeHistoryOccurrenceMembershipEntity.Ordinal)));
        Assert.Throws<InvalidOperationException>(() =>
        { _ = context.Set<RuntimeHistoryOccurrenceMembershipEntity>().Where(row => row.Ordinal >= 0).Take(1).ToQueryString(); });
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(writer.CanCommitHistory(reader)); Assert.False(observer.CanRecordHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Model_mappings_that_ignore_supplied_history_content_cannot_qualify_pairing(bool insert)
    {
        using RuntimeDbContext context = insert
            ? new IgnoredHistoryInsertContext(new DbContextOptionsBuilder<IgnoredHistoryInsertContext>().UseSqlite("Data Source=:memory:").Options)
            : new IgnoredHistoryUpdateContext(new DbContextOptionsBuilder<IgnoredHistoryUpdateContext>().UseSqlite("Data Source=:memory:").Options);
        var entityType = insert ? typeof(RuntimeHistoryEventEntity) : typeof(RuntimeHistoryBookmarkEntity);
        var property = context.Model.FindEntityType(entityType)!.FindProperty("ContentJson"); Assert.NotNull(property);
        Assert.Equal(PropertySaveBehavior.Ignore, insert ? property.GetBeforeSaveBehavior() : property.GetAfterSaveBehavior());
        var access = new HistoryCapabilityAccess(); var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(writer.CanCommitHistory(reader)); Assert.False(observer.CanRecordHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class HistoryCapabilityAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; init; } = PersistenceAccessContext.Scoped(new("tenant-a")); }

    private sealed class IncompleteHistoryCapabilityContext(DbContextOptions<IncompleteHistoryCapabilityContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Ignore<RuntimeHistoryOccurrenceMembershipEntity>();
        }
    }

    private sealed class MissingOrdinalCapabilityContext(DbContextOptions<MissingOrdinalCapabilityContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryOccurrenceMembershipEntity>().Ignore(row => row.Ordinal);
        }
    }

    private sealed class IgnoredHistoryInsertContext(DbContextOptions<IgnoredHistoryInsertContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryEventEntity>().Property(row => row.ContentJson).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }
    private sealed class IgnoredHistoryUpdateContext(DbContextOptions<IgnoredHistoryUpdateContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryBookmarkEntity>().Property(row => row.ContentJson).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }
}
