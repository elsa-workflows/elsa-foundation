using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Foreign_history_scalar_conversion_cannot_qualify_paired_history(bool independent)
    {
        using var context = new ForeignHistoryScalarConversionContext(new DbContextOptionsBuilder<ForeignHistoryScalarConversionContext>().UseSqlite("Data Source=:memory:").Options);
        var access = new HistoryCapabilityAccess();
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(independent ? observer.CanRecordHistory(reader) : writer.CanCommitHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class ForeignHistoryScalarConversionContext(DbContextOptions<ForeignHistoryScalarConversionContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryEventEntity>().Property(row => row.Name).HasConversion(
                new ValueConverter<string, string>(value => "discarded", value => value));
        }
    }
}
