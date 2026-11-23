using DotNet.Testcontainers.Containers;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Studio.Dashboard.Persistence.ProviderTests;

public sealed class DashboardProviderTests
{
    [SkippableTheory]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task Owned_provider_applies_actual_migrations_and_preserves_keyed_cas_history_and_revocation(string name)
    {
        await using var lease = await OwnedDatabase.StartAsync(name);
        if (Environment.GetEnvironmentVariable("ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX") == "1")
            Assert.True(lease.Available, lease.UnavailableReason ?? "The required owned provider is unavailable.");
        Skip.IfNot(lease.Available, lease.UnavailableReason ?? "The owned provider is unavailable.");
        await RunAsync(name, lease.ConnectionString!);
    }

    private static async Task RunAsync(string providerName, string connectionString)
    {
        var owner = Actor("owner"); var viewer = Actor("viewer");
        await using var provider = Provider(providerName, connectionString);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DashboardResourceDbContext>();
            await context.Database.MigrateAsync();
            Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        }
        var input = new DashboardResourceInput { Name = "Provider question", OperationKey = "create-once",
            Definition = new() { Query = new() { Dataset = new("fixture.records", "1"), Form = "records", Fields = ["id"] } } };
        var saved = await Use(provider, service => service.CreateAsync(owner, "question", input));
        Assert.Equal(saved.Id, (await Use(provider, service => service.CreateAsync(owner, "question", input))).Id);
        var racedInput = input with { OperationKey = "concurrent-create" };
        var concurrentCreates = await Task.WhenAll(
            Use(provider, service => service.CreateAsync(owner, "question", racedInput)),
            Use(provider, service => service.CreateAsync(owner, "question", racedInput)));
        Assert.Equal(concurrentCreates[0].Id, concurrentCreates[1].Id);
        var raceQuestion = concurrentCreates[0];
        async Task<string> Compete(string name)
        {
            try
            {
                await Use(provider, service => service.UpdateAsync(owner, "question", raceQuestion.Id, raceQuestion.Revision,
                    new() { Name = name, PreserveDefinition = true }));
                return "saved";
            }
            catch (DashboardResourceException error) { return error.Code; }
        }
        var writeRace = await Task.WhenAll(Compete("First writer"), Compete("Second writer"));
        Assert.Single(writeRace, result => result == "saved");
        Assert.Single(writeRace, result => result == "conflict");
        saved = await Use(provider, service => service.SetAudienceAsync(owner, "question", saved.Id, saved.Revision, [new("user", "viewer", "viewer")]));
        Assert.True((await Use(provider, service => service.GetAsync(viewer, "question", saved.Id))).Capabilities.CanRead);
        var stale = saved.Revision;
        saved = await Use(provider, service => service.UpdateAsync(owner, "question", saved.Id, stale,
            new() { Name = "Durable revised question", PreserveDefinition = true }));
        var conflict = await Assert.ThrowsAsync<DashboardResourceException>(() => Use(provider, service =>
            service.UpdateAsync(owner, "question", saved.Id, stale, new() { Name = "Stale", PreserveDefinition = true })));
        Assert.Equal("conflict", conflict.Code);
        var copy = await Use(provider, service => service.CopyAsync(viewer, "question", saved.Id, saved.Revision, new("copy-once", "Private copy")));
        Assert.Empty((await Use(provider, service => service.GetAudienceAsync(viewer, "question", copy.Id))).Items);
        saved = await Use(provider, service => service.SetAudienceAsync(owner, "question", saved.Id, saved.Revision, []));
        Assert.Equal("notFound", (await Assert.ThrowsAsync<DashboardResourceException>(() => Use(provider,
            service => service.HistoryAsync(viewer, "question", saved.Id)))).Code);
        await using var restarted = Provider(providerName, connectionString);
        var reread = await Use(restarted, service => service.GetAsync(owner, "question", saved.Id));
        Assert.Equal("Durable revised question", reread.Name);
        Assert.Equal(saved.Revision, reread.Revision);
        Assert.Equal(copy.Id, (await Use(restarted, service => service.GetAsync(viewer, "question", copy.Id))).Id);
        Assert.Equal(saved.Id, (await Use(restarted, service => service.CreateAsync(owner, "question", input))).Id);
        Assert.True((await Use(restarted, service => service.HistoryAsync(owner, "question", saved.Id))).Items.Count > 1);
        for (var index = 0; index < 17; index++)
            await Use(restarted, service => service.CreateAsync(owner, "question", input with { OperationKey = "page-" + index, Name = "Page " + index }));
        Assert.Equal(19, (await Use(restarted, service => service.ListAsync(owner, "question"))).Items.Count);
        Assert.Single((await Use(restarted, service => service.ListAsync(viewer, "question"))).Items);
        await using var pageScope = restarted.CreateAsyncScope();
        var store = pageScope.ServiceProvider.GetRequiredService<IDashboardResourceStore>();
        var firstPage = await store.ReadPageAsync(owner.TenantId, "question");
        var lastPage = await store.ReadPageAsync(owner.TenantId, "question", firstPage[^1].Id);
        Assert.Equal(16, firstPage.Count); Assert.Equal(4, lastPage.Count);
        Assert.Empty(firstPage.Select(item => item.Id).Intersect(lastPage.Select(item => item.Id)));
        Assert.All(firstPage.Concat(lastPage), item => Assert.Empty(item.History));
    }

    private static ServiceProvider Provider(string name, string connection)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IDashboardDefinitionValidator, FixtureDefinitionValidator>();
        services.AddSingleton<IDashboardAudienceDirectory, FixtureDirectory>();
        services.AddDashboardResources();
        services.AddDashboardResourcesEntityFrameworkCore(new() { Provider = name, ConnectionString = connection });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    private static DashboardActor Actor(string id) => new("owned-tenant", id, new HashSet<string>(), new HashSet<string>(["*"]));
    private static async Task<T> Use<T>(ServiceProvider provider, Func<IDashboardResourceService, ValueTask<T>> action)
    { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IDashboardResourceService>()); }
}

internal sealed class FixtureDefinitionValidator : IDashboardDefinitionValidator
{
    public ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
}
internal sealed class FixtureDirectory : IDashboardAudienceDirectory
{
    public ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(grant.Kind == "user" && grant.Id == "viewer");
    public ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search,
        string? continuationToken = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(new DashboardAudienceCandidatePage([]));
}

internal sealed class OwnedDatabase : IAsyncDisposable
{
    private IContainer? container;
    public bool Available { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public static async Task<OwnedDatabase> StartAsync(string provider)
    {
        var lease = new OwnedDatabase();
        var name = $"elsa-3604-dashboard-{provider.ToLowerInvariant()}-{Guid.NewGuid():N}";
        try
        {
            switch (provider)
            {
                case "PostgreSql":
                    var postgres = new PostgreSqlBuilder("postgres:16-alpine").WithName(name).WithDatabase("dashboard_owned").Build();
                    lease.container = postgres; await postgres.StartAsync(); lease.ConnectionString = postgres.GetConnectionString(); break;
                case "MySql":
                    var mysql = new MySqlBuilder("mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a")
                        .WithName(name).WithDatabase("dashboard_owned").Build();
                    lease.container = mysql; await mysql.StartAsync(); lease.ConnectionString = mysql.GetConnectionString(); break;
                case "SqlServer":
                    var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").WithName(name).Build();
                    lease.container = sql; await sql.StartAsync(); lease.ConnectionString = sql.GetConnectionString(); break;
                default: throw new ArgumentException("Unknown provider.", nameof(provider));
            }
            lease.Available = true;
        }
        catch (Exception error) when (DockerUnavailable(error))
        { lease.UnavailableReason = $"Owned {provider} container unavailable ({error.GetType().Name})."; }
        return lease;
    }
    private static bool DockerUnavailable(Exception error) => error.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        error.InnerException is not null && DockerUnavailable(error.InnerException);
    public async ValueTask DisposeAsync() { if (container is not null) await container.DisposeAsync(); }
}
