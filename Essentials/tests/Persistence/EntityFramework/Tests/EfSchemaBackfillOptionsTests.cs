using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>The backfill's settings (spec 186, FR-009, FR-012, FR-018 and FR-023), from the container's configuration: defaults, overrides, refusals.</summary>
public sealed class EfSchemaBackfillOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_and_leave_the_settle_margin_to_the_fleet()
    {
        var options = Configure(new Dictionary<string, string?>());

        Assert.Equal((500, TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2)),
            (options.BatchSize, options.BatchPause, options.AuditInterval, options.RepairableBlockerInterval, options.ClaimDuration));
        Assert.Null(options.SettleMargin);
    }

    [Fact]
    public void Configuration_overrides_each_setting()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            [$"{EfSchemaBackfillOptions.SectionName}:BatchSize"] = "50",
            [$"{EfSchemaBackfillOptions.SectionName}:BatchPause"] = "00:00:01",
            [$"{EfSchemaBackfillOptions.SectionName}:AuditInterval"] = "00:10:00",
            [$"{EfSchemaBackfillOptions.SectionName}:RepairableBlockerInterval"] = "00:00:30",
            [$"{EfSchemaBackfillOptions.SectionName}:SettleMargin"] = "00:01:00",
            [$"{EfSchemaBackfillOptions.SectionName}:ClaimDuration"] = "00:00:00"
        });

        Assert.Equal((50, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.Zero),
            (options.BatchSize, options.BatchPause, options.AuditInterval, options.RepairableBlockerInterval, options.SettleMargin, options.ClaimDuration));
    }

    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "many")]
    [InlineData("AuditInterval", "00:00:00")]
    [InlineData("RepairableBlockerInterval", "00:00:00")]
    [InlineData("SettleMargin", "-00:00:01")]
    [InlineData("CheckInterval", "soon")]
    public void A_setting_that_would_stop_the_backfill_or_is_not_a_value_is_refused(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Configure(new Dictionary<string, string?> { [$"{EfSchemaBackfillOptions.SectionName}:{key}"] = value }));

    private static EfSchemaBackfillOptions Configure(IDictionary<string, string?> settings)
    {
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();
        var options = new EfSchemaBackfillOptions();
        new EfSchemaBackfillOptionsConfigurator(services).Configure(options);
        return options;
    }
}
