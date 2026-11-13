using System.Data;
using Elsa.Workflows.Publishing.Releases;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests;

public sealed class ReleaseArchiveCompositionTests
{
    [Fact]
    public void Competing_selection_refuses_without_removing_the_original_descriptors()
    {
        var services = Configured();
        services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
        var original = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope));
        Assert.Equal(original, services.ToArray());
    }

    [Fact]
    public async Task Deliberate_replacement_selects_all_three_ports_together()
    {
        var services = Configured();
        var oldCalls = 0; var newCalls = 0;
        services.AddReleaseArchiveStore<ConsumerContext>(_ => { oldCalls++; return ArchiveFixture.DefaultScope; });
        services.AddReleaseArchiveStore<ConsumerContext>(_ => { newCalls++; return ArchiveFixture.DefaultScope; }, replaceExisting: true);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        Assert.Same(reader, scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>());
        Assert.Same(reader, scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>());
        Assert.Equal(0, oldCalls); Assert.Equal(1, newCalls);
    }

    [Theory]
    [InlineData("reader", false)] [InlineData("writer", false)] [InlineData("capabilities", false)]
    [InlineData("reader", true)] [InlineData("writer", true)] [InlineData("capabilities", true)]
    public void Startup_refuses_later_duplicates_and_partial_replacement_before_any_factory_runs(string port, bool replace)
    {
        var services = Configured();
        services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
        var calls = 0;
        var descriptor = ServiceDescriptor.Scoped(Port(port), _ => { calls++; throw new InvalidOperationException("factory must not run"); });
        if (replace) services.Replace(descriptor); else services.Add(descriptor);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("reader")] [InlineData("writer")] [InlineData("capabilities")]
    public void Default_facade_refuses_incoherent_ports_before_resolving_the_context(string port)
    {
        var services = new ServiceCollection();
        var contextCalls = 0;
        services.AddScoped<ConsumerContext>(_ => { contextCalls++; throw new InvalidOperationException("context must not resolve"); });
        services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
        services.Add(ServiceDescriptor.Scoped(Port(port), _ => throw new InvalidOperationException("override must not resolve")));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var retainedPort = port == "reader" ? typeof(IReleaseArchiveWriter) : typeof(IReleaseArchiveReader);
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService(retainedPort));
        Assert.Contains("coherent", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, contextCalls);
    }

    [Fact]
    public void Explicit_replacement_does_not_clear_an_already_split_selection()
    {
        var services = Configured();
        services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
        services.Replace(ServiceDescriptor.Scoped<IReleaseArchiveReader>(_ => throw new InvalidOperationException("must not resolve")));
        var original = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope, replaceExisting: true));
        Assert.Equal(original, services.ToArray());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Explicit_connections_are_refused_without_enlisting_or_disposing_the_callers_connection(bool contextOwnsConnection)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = Configured(options => options.UseSqlite(connection, contextOwnsConnection));
        services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        await using var scope = provider.CreateAsyncScope();
        var caller = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
        await using var physical = connection.BeginTransaction(deferred: true);
        await using var enlisted = await caller.Database.UseTransactionAsync(physical);
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        var capabilities = scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>();
        var readError = await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => reader.FindAsync("capture").AsTask());
        var writeError = await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => writer.CreateAsync("capture", new MemoryStream(new ArchiveFixture().Zip())).AsTask());
        var observeError = await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => capabilities.GetCapabilitiesAsync().AsTask());
        foreach (var error in new[] { readError, writeError, observeError }) Assert.Equal("archive_shared_connection_unsupported", error.Code);
        Assert.Same(connection, caller.Database.GetDbConnection());
        Assert.Same(enlisted, caller.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, connection.State);
        await using var command = connection.CreateCommand(); command.Transaction = physical; command.CommandText = "SELECT 1";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await physical.RollbackAsync();
    }

    private static Type Port(string name) => name switch
    {
        "reader" => typeof(IReleaseArchiveReader), "writer" => typeof(IReleaseArchiveWriter), _ => typeof(IReleaseArchiveStoreCapabilities)
    };

    private static ServiceCollection Configured(Action<DbContextOptionsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        new WorkflowReleaseTransportFeature().ConfigureServices(services);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new MutableAccess(PersistenceAccessContext.Scoped(new PersistenceScope(ArchiveFixture.DefaultScope.Partition))));
        services.AddDbContext<ConsumerContext>(configure ?? (options => options.UseSqlite("Data Source=:memory:")));
        return services;
    }
}
