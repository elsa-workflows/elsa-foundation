using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void Effective_mapping_or_provider_type_conversion_cannot_bypass_history_pairing(bool independent, bool mapping)
    {
        using var context = EffectiveConversionContext(mapping);
        var property = context.Model.FindEntityType(typeof(RuntimeHistoryEventEntity))!.FindProperty(nameof(RuntimeHistoryEventEntity.Name))!;
        Assert.Null(((Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty)property).GetValueConverter());
        Assert.NotNull(property.GetTypeMapping().Converter);
        var access = new HistoryCapabilityAccess();
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(independent ? observer.CanRecordHistory(reader) : writer.CanCommitHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Effective_conversion_changes_or_refuses_actual_native_history_name_storage(bool mapping)
    {
        await using var context = EffectiveConversionContext(mapping);
        await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
        context.Set<RuntimeHistoryEventEntity>().Add(new()
        {
            Id = "event", ScopeKey = "tenant-a", ScopeKeyHash = new string('a', 64), WorkflowExecutionId = "workflow-a",
            WorkflowExecutionIdHash = new string('b', 64), ContentJson = "{}", SchemaVersion = "1.0.0", Revision = 1,
            HistoryEventId = "history-event", HistoryEventIdHash = new string('c', 64), Name = "ActivityStarted",
            NameHash = new string('d', 64), CandidateFingerprint = new string('e', 64), Sequence = 1
        });
        if (mapping)
        {
            await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            Assert.Equal("discarded", (await context.Set<RuntimeHistoryEventEntity>().SingleAsync()).Name);
        }
        else
        {
            var error = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            Assert.NotNull(error);
            Assert.Contains(HistoryFailureCauses(error), cause => cause is FormatException);
        }
    }

    private static RuntimeDbContext EffectiveConversionContext(bool mapping) => mapping
        ? new ComposedHistoryMappingContext(new DbContextOptionsBuilder<ComposedHistoryMappingContext>().UseSqlite("Data Source=:memory:").Options)
        : new ProviderTypeHistoryConversionContext(new DbContextOptionsBuilder<ProviderTypeHistoryConversionContext>().UseSqlite("Data Source=:memory:").Options);

    private sealed class ProviderTypeHistoryConversionContext(DbContextOptions<ProviderTypeHistoryConversionContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryEventEntity>().Property(row => row.Name).HasConversion<int>();
        }
    }

    private sealed class ComposedHistoryMappingContext(DbContextOptions<ComposedHistoryMappingContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var mapping = new StringTypeMapping("TEXT", System.Data.DbType.String).WithComposedConverter(
                new ValueConverter<string, string>(value => "discarded", value => value));
            builder.Entity<RuntimeHistoryEventEntity>().Property(row => row.Name).Metadata.SetTypeMapping(mapping);
        }
    }
}
