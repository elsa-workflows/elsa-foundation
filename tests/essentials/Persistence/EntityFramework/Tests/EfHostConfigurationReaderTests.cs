using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// How a host reads the settings of an EF store it composes on its own container, as cluster membership and the Data
/// Protection key store do: a blank value keeps the default, a value that does not parse is refused naming its key, and a
/// store section with settings but no Enabled switch is refused rather than taken as off, each with the exception the
/// reader was built with.
/// </summary>
public sealed class EfHostConfigurationReaderTests
{
    private const string Store = "Elsa:Store";

    private readonly EfHostConfigurationReader _reader = new();

    [Fact]
    public void Blank_values_keep_the_defaults()
    {
        var section = Section(new() { [$"{Store}:Pooling"] = "", [$"{Store}:Period"] = " " });

        var options = _reader.ReadStore(section, new TestStoreOptions { Pooling = true });

        Assert.True(options.Pooling);
        Assert.Equal(EfProviderNames.Sqlite, options.Provider);
        Assert.Null(_reader.ReadTimeSpan(section, "Period"));
    }

    [Fact]
    public void The_store_settings_are_read_into_the_options()
    {
        var section = Section(new()
        {
            [$"{Store}:Provider"] = "PostgreSql",
            [$"{Store}:ConnectionString"] = "Host=db",
            [$"{Store}:ConnectionName"] = "Named",
            [$"{Store}:Schema"] = "elsa",
            [$"{Store}:Pooling"] = "true",
            [$"{Store}:Period"] = "00:00:30"
        });

        var options = _reader.ReadStore(section, new TestStoreOptions());

        Assert.Equal(("PostgreSql", "Host=db", "Named", "elsa", true), (options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling));
        Assert.Equal(TimeSpan.FromSeconds(30), _reader.ReadTimeSpan(section, "Period"));
    }

    [Theory]
    [InlineData("Enabled", "yes", "true or false")]
    [InlineData("Pooling", "sometimes", "true or false")]
    [InlineData("Period", "half a minute", "a time span")]
    public void A_value_that_does_not_parse_is_refused_naming_its_key(string key, string value, string expected)
    {
        var section = Section(new() { [$"{Store}:{key}"] = value });

        var failure = Assert.Throws<EfHostConfigurationException>(() => Read(_reader, section, key));

        Assert.Contains($"{Store}:{key} is '{value}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A host that meant to enable a store and silently did not is the failure that looks like success.</summary>
    [Fact]
    public void Settings_without_the_Enabled_switch_are_refused()
    {
        var section = Section(new() { [$"{Store}:Provider"] = "PostgreSql" });

        var failure = Assert.Throws<EfHostConfigurationException>(() => _reader.IsEnabled(section, "to enable it", "to leave it off"));

        Assert.Equal(
            $"{Store} carries settings but no Enabled switch. Set {Store}:Enabled to true to enable it, or to false to leave it off.",
            failure.Message);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_Enabled_switch_turns_the_store_on_or_off(string value, bool expected)
    {
        var section = Section(new() { [$"{Store}:Enabled"] = value, [$"{Store}:Provider"] = "PostgreSql" });

        Assert.Equal(expected, _reader.IsEnabled(section, "to enable it", "to leave it off"));
    }

    [Fact]
    public void An_empty_section_is_off()
    {
        Assert.False(_reader.IsEnabled(Section(new()), "to enable it", "to leave it off"));
    }

    /// <summary>
    /// A feature whose contract names its own exception refuses with that one, for every kind of refusal: settings without
    /// the switch, and a switch, a pooling setting or a time span that does not parse.
    /// </summary>
    [Theory]
    [InlineData("Provider", "PostgreSql")]
    [InlineData("Enabled", "yes")]
    [InlineData("Pooling", "sometimes")]
    [InlineData("Period", "half a minute")]
    public void Refusals_are_raised_with_the_exception_the_reader_was_built_with(string key, string value)
    {
        var reader = new EfHostConfigurationReader(message => new ContractException(message));
        var section = Section(new() { [$"{Store}:{key}"] = value });

        var failure = Assert.Throws<ContractException>(() => Read(reader, section, key));

        Assert.StartsWith(Store, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Reads <paramref name="key"/> the way the reader reads it: the switch, a time span, or a store setting.</summary>
    private static object? Read(EfHostConfigurationReader reader, IConfigurationSection section, string key) => key switch
    {
        "Enabled" or "Provider" => reader.IsEnabled(section, "to enable it", "to leave it off"),
        "Period" => reader.ReadTimeSpan(section, key),
        _ => reader.ReadStore(section, new TestStoreOptions())
    };

    private static IConfigurationSection Section(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection(Store);

    private sealed class TestStoreOptions : EfHostStoreOptions;

    private sealed class ContractException(string message) : Exception(message);
}
