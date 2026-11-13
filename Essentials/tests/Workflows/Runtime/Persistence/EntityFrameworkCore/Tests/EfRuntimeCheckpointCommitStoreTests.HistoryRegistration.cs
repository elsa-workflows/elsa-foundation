using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    public void Public_native_registration_selects_one_scoped_history_reader_without_enabling_capture()
    {
        var services = CompleteHistoryReaderServices(); services.AddRuntimeCheckpointCommitEntityFrameworkCore(); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryStore));
        using var provider = services.BuildServiceProvider(); using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var reader = first.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>(); Assert.IsType<EfRuntimeHistoryStore>(reader);
        Assert.IsAssignableFrom<IRuntimeHistoryDetailsStore>(reader); Assert.IsAssignableFrom<IRuntimeHistoryOccurrenceStore>(reader);
        Assert.Same(reader, first.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>());
        Assert.NotSame(reader, second.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>());
        var writer = first.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        Assert.False(writer.SupportsHistory); Assert.False(writer.CanCommitHistory(reader));
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)] [InlineData(ServiceLifetime.Scoped)]
    public void Native_registration_preserves_an_explicit_foreign_history_reader(ServiceLifetime lifetime)
    {
        var services = CompleteEfServices(); var owned = new ServiceDescriptor(typeof(IRuntimeHistoryStore),
            _ => throw new InvalidOperationException("This foreign factory must not be invoked during selection."), lifetime);
        ((IServiceCollection)services).Add(owned); services.AddRuntimeCheckpointCommitEntityFrameworkCore(); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        Assert.Same(owned, Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryStore))));
    }

    [Fact]
    public async Task Registered_reader_inspects_the_same_actual_native_checkpoint_history()
    {
        var connection = $"Data Source=file:ef-history-di-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connection); await keeper.OpenAsync();
        var services = CompleteHistoryReaderServices(connection); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("tenant-a")));
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>(); await context.Database.EnsureCreatedAsync();
        var state = Execution("workflow-a", "tenant-a") with { HistoryCapturePolicy = NativeHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
        await context.SaveChangesAsync();
        var writer = scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        await writer.CommitAsync(NativeHistoryCommit("registered-history"), Decision());
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var head = await reader.FindHeadAsync("workflow-a"); Assert.NotNull(head); Assert.Equal(2, head.LastSequence);
        var details = await ((IRuntimeHistoryDetailsStore)reader).FindEventDetailsAsync("workflow-a", "created");
        Assert.NotNull(details.Event); Assert.Equal("registered-history", details.Event.PhysicalCommitId); Assert.Equal(2, details.Head!.LastSequence);
        var bookmarks = await reader.ListBookmarksAsync(new("workflow-a"));
        Assert.Equal("history-bookmark", Assert.Single(bookmarks.Items).Bookmark.Identity.BookmarkId);
        Assert.Single(await context.Bookmarks.ToArrayAsync()); Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private static ServiceCollection CompleteHistoryReaderServices(string connection = "Data Source=:memory:")
    {
        var services = CompleteEfServices(connection);
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "history-reader-registration-controls-0123456789");
        return services;
    }
}
