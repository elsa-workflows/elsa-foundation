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
}
