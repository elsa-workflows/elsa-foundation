using CShells.Lifecycle;
using CShells.Nuplane;
using Elsa.Persistence.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FoundationHost = Elsa.Foundation.Host.Shells;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class NuplaneReloadResultCallbackTests
{
    [Fact(DisplayName = "Foundation reports mixed reload results with an actionable refusal and the original ordinary exception")]
    public async Task Foundation_callback_ReportsMixedResults_AndPreservesOrdinaryException()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("true");
        var logger = new CapturingLogger();
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: true, logger: logger);
        var callback = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue.OnReloadResults!;
        var ordinaryFailure = new InvalidOperationException("private connection string");
        ReloadResult[] results =
        [
            new("healthy", null, null, null),
            new("orders", null, null, new AggregateException(new InvalidOperationException("wrapper", new PendingRefusal()))),
            new("broken", null, null, ordinaryFailure)
        ];

        await callback(results, CancellationToken.None);

        Assert.Contains(logger.Entries, entry => entry is { Level: LogLevel.Information } && entry.Message.Contains("Reloaded 1 active shell(s)", StringComparison.Ordinal));
        var refusal = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("orders", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"--host \"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\" --modules Orders", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IEfModuleRefusal.HostPlaceholder, refusal.Message, StringComparison.Ordinal);
        var errorIndex = logger.Entries.ToList().FindIndex(entry => entry.Level == LogLevel.Error);
        Assert.True(errorIndex >= 0);
        Assert.Same(ordinaryFailure, logger.Exceptions[errorIndex]);
        Assert.Contains("broken", logger.Entries[errorIndex].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private connection string", logger.Entries[errorIndex].Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Foundation reports all failed shells without counting a reload as successful")]
    public async Task Foundation_callback_ReportsAllFailedResults_WithZeroSuccessfulReloads()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("true");
        var logger = new CapturingLogger();
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: true, logger: logger);
        var callback = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue.OnReloadResults!;
        var first = new InvalidOperationException("first failure");
        var second = new InvalidOperationException("second failure");

        await callback(
        [
            new("first", null, null, first),
            new("second", null, null, second)
        ], CancellationToken.None);

        Assert.Contains(logger.Entries, entry => entry is { Level: LogLevel.Information } && entry.Message.Contains("Reloaded 0 active shell(s)", StringComparison.Ordinal));
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.Equal(new Exception[] { first, second }, logger.Exceptions.Where(exception => exception is not null).Cast<Exception>());
    }

    [Fact(DisplayName = "Foundation substitutes the host path for a refusal from a private contract copy")]
    public async Task Foundation_callback_RecognizesForeignRefusal_AndSubstitutesHostDirectory()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("true");
        var logger = new CapturingLogger();
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: true, logger: logger);
        var callback = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue.OnReloadResults!;
        var foreign = FoundationHostReloadEndpointTests.ForeignRefusal.Create(
            $"Apply this refusal with dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders --provider Sqlite --connection-env ELSA_EF_CONNECTION.");

        await callback([new ReloadResult("orders", null, null, foreign)], CancellationToken.None);

        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("orders", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"--host \"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\" --modules Orders --provider Sqlite", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IEfModuleRefusal.HostPlaceholder, warning.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Workbench reads nested aggregate refusals and substitutes the host directory")]
    public async Task Workbench_callback_FindsNestedAggregateRefusal_AndSubstitutesHostDirectory()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("true");
        var logger = new CapturingLogger();
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: false, logger: logger);
        var callback = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue.OnReloadResults!;

        await callback(
        [
            new("orders", null, null, new AggregateException(
                "outer",
                new InvalidOperationException("middle", new AggregateException(new PendingRefusal()))))
        ], CancellationToken.None);

        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("orders", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"--host \"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\" --modules Orders", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IEfModuleRefusal.HostPlaceholder, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "Workbench keeps ordinary activation exceptions intact in host logs")]
    public async Task Workbench_callback_LogsOrdinaryFailure_WithOriginalException()
    {
        using var configuration = NuplaneHostTestComposition.CreateConfiguration("true");
        var logger = new CapturingLogger();
        using var services = NuplaneHostTestComposition.BuildProfile(configuration, foundation: false, logger: logger);
        var callback = services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue.OnReloadResults!;
        var failure = new InvalidOperationException("provider construction failed");

        await callback([new ReloadResult("default", null, null, failure)], CancellationToken.None);

        var errorIndex = logger.Entries.ToList().FindIndex(entry => entry.Level == LogLevel.Error);
        Assert.True(errorIndex >= 0);
        Assert.Same(failure, logger.Exceptions[errorIndex]);
        Assert.Contains("default", logger.Entries[errorIndex].Message, StringComparison.Ordinal);
    }



    private sealed class PendingRefusal() : InvalidOperationException(
        $"EF module 'Orders' has pending migrations. Apply them out of process: dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders"), IEfModuleRefusal
    {
        public string Module => "Orders";

        public string Code => IEfModuleRefusal.PendingMigrationsCode;

        public IReadOnlyList<string> PendingMigrations => ["20260930_One"];

        public string? Command => $"dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders";
    }
}
