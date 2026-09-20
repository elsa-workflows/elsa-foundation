using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfConnectionDefaultsTests
{
    [Fact]
    public void An_explicit_connection_string_wins_over_every_configured_entry()
    {
        var services = Configured(("Named", "Data Source=named.db"), (EfConnectionDefaults.ConnectionName, "Data Source=default.db"));

        Assert.Equal("Data Source=explicit.db", Resolve(services, "Sqlite", "Data Source=explicit.db", "Named"));
    }

    [Fact]
    public void A_named_entry_wins_over_the_default_entry()
    {
        var services = Configured(("Named", "Data Source=named.db"), (EfConnectionDefaults.ConnectionName, "Data Source=default.db"));

        Assert.Equal("Data Source=named.db", Resolve(services, "SqlServer", null, "Named"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_missing_or_blank_named_entry_is_refused_instead_of_falling_back_to_a_default(string? configured)
    {
        var services = Configured(("Missing", configured), (EfConnectionDefaults.ConnectionName, "Data Source=default.db"));

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(services, "Sqlite", null, "Missing"));
        Assert.Equal("Test EF connection 'Missing' was not found or was empty in ConnectionStrings.", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void The_default_entry_is_used_when_no_connection_is_named(string? connectionName)
    {
        var services = Configured((EfConnectionDefaults.ConnectionName, "Data Source=default.db"));

        Assert.Equal("Data Source=default.db", Resolve(services, "PostgreSql", " ", connectionName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_default_entry_falls_through_to_the_sqlite_file(string configured)
    {
        var services = Configured((EfConnectionDefaults.ConnectionName, configured));

        Assert.Equal(EfConnectionDefaults.SqliteConnectionString, Resolve(services, "Sqlite", null, null));
    }

    [Fact]
    public void A_module_default_entry_and_sqlite_file_replace_the_shared_defaults()
    {
        var configured = Configured(("Module", "Data Source=module.db"), (EfConnectionDefaults.ConnectionName, "Data Source=shared.db"));

        Assert.Equal("Data Source=module.db", EfConnectionDefaults.ResolveConnectionString(configured, "Test", "Sqlite", null, null, "Module", "Data Source=module-file.db"));
        Assert.Equal("Data Source=module-file.db", EfConnectionDefaults.ResolveConnectionString(Configured(), "Test", "Sqlite", null, null, "Module", "Data Source=module-file.db"));
    }

    [Fact]
    public void A_non_sqlite_provider_without_a_connection_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new ConfigurationServices(null), "MySql", null, null));

        Assert.Equal("Test EF requires ConnectionString or ConnectionName, or ConnectionStrings:Elsa, for a non-Sqlite provider.", exception.Message);
    }

    private static string Resolve(IServiceProvider services, string provider, string? connectionString, string? connectionName) =>
        EfConnectionDefaults.ResolveConnectionString(services, "Test", provider, connectionString, connectionName);

    private static ConfigurationServices Configured(params (string Name, string? Value)[] connections) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(connections.Select(connection =>
                new KeyValuePair<string, string?>($"ConnectionStrings:{connection.Name}", connection.Value)))
            .Build());
}
