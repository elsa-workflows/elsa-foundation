using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed class JobsRegistrationTests
{
    [Fact]
    public async Task Retained_mutable_options_cannot_switch_connection_or_schema_between_scopes()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:first"] = "Host=first.invalid;Database=jobs;Username=none",
            ["ConnectionStrings:second"] = "Host=second.invalid;Database=jobs;Username=none"
        }).Build());
        JobsEntityFrameworkCoreOptions? captured = null;
        services.AddJobsEntityFrameworkCore(options =>
        {
            captured = options; options.Provider = "PostgreSql"; options.ConnectionName = "first"; options.Schema = "first_schema";
        });
        services.ConfigureDbContext<JobsPostgreSqlDbContext>(options => options.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        await using var provider = services.BuildServiceProvider();
        await using (var first = provider.CreateAsyncScope())
        {
            await using var db = await first.ServiceProvider.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContextAsync();
            Assert.Contains("first.invalid", db.Database.GetConnectionString()); Assert.Equal("first_schema", db.Model.GetDefaultSchema());
        }
        captured!.ConnectionName = "second"; captured.ConnectionString = "Host=third.invalid;Database=jobs;Username=none";
        captured.Schema = "changed_schema"; captured.Provider = "Sqlite";
        await using var second = provider.CreateAsyncScope();
        await using var sameTarget = await second.ServiceProvider.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContextAsync();
        Assert.Contains("first.invalid", sameTarget.Database.GetConnectionString()); Assert.Equal("first_schema", sameTarget.Model.GetDefaultSchema());
    }

    [Fact]
    public void Duplicate_composition_is_refused_before_replacing_factories_or_settings()
    {
        var services = new ServiceCollection(); services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=first.db");
        var before = services.ToArray();
        var failure = Assert.Throws<JobException>(() => services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=second.db"));
        Assert.Equal("jobs.persistence.duplicate-authority", failure.Code); Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Query_default_follows_an_explicit_command_replacement()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=unused.db");
        services.Replace(ServiceDescriptor.Scoped<IJobCommands>(_ => throw new SelectedReplacementReached()));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Throws<SelectedReplacementReached>(() => scope.ServiceProvider.GetRequiredService<IJobQueries>());
    }

    private sealed class SelectedReplacementReached : Exception { }

    [Fact]
    public void Multiple_recurrence_replacements_are_refused_when_the_authority_resolves()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddJobsEntityFrameworkCore(options => options.ConnectionString = "Data Source=unused.db");
        services.AddSingleton<IJobRecurrenceCalculator>(new JobsProviderDatabase.IntervalCalculator());
        services.AddSingleton<IJobRecurrenceCalculator>(new JobsProviderDatabase.IntervalCalculator());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var failure = Assert.Throws<JobException>(() => scope.ServiceProvider.GetRequiredService<IJobCommands>());
        Assert.Equal("jobs.recurrence.duplicate-authority", failure.Code);
    }
}
