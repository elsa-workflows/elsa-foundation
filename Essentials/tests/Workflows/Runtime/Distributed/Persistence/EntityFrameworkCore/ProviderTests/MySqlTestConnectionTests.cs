using MySql.Data.MySqlClient;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Guards the one property <see cref="MySqlTestConnection"/> exists for: this assembly's fixtures must not make the
/// connector negotiate TLS. The race in #1854 lives inside that negotiation, so "no negotiation" is the fix, and a
/// later tidy-up that restores the default <c>SslMode</c> would silently restore the race. Asserting the server's
/// own view of the session is what makes this a check rather than a comment.
/// </summary>
[Collection(RuntimePlacementMySqlContainerFixture.CollectionName)]
public sealed class MySqlTestConnectionTests(RuntimePlacementMySqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Fixture_connection_strings_do_not_negotiate_tls()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");

        Assert.Equal(string.Empty, await SessionCipherAsync(fixture.ConnectionString));
    }

    /// <summary>
    /// The same container without the hardening, to show the guard above is actually load-bearing: the connector's
    /// default <c>SslMode</c> negotiates TLS, which is the code path that carries the unsynchronized dictionary.
    /// </summary>
    [SkippableFact]
    public async Task Connector_default_would_negotiate_tls_against_the_same_container()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");

        var builder = new MySqlConnectionStringBuilder(fixture.ConnectionString) { SslMode = MySqlSslMode.Preferred };

        Assert.NotEqual(string.Empty, await SessionCipherAsync(builder.ConnectionString));
    }

    internal static async Task<string> SessionCipherAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW STATUS LIKE 'Ssl_cipher'";
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetString(1) : string.Empty;
    }
}

/// <summary>
/// The same guard for the assembly's other MySQL collection. The race needs both fixtures negotiating at once, so
/// the transport half of the fix is exactly as load-bearing as the placement half — and without this, deleting
/// <c>MySqlTestConnection.WithoutSsl</c> from <c>RuntimeCommandTransportMySqlContainerFixture</c> left the whole
/// suite green.
/// </summary>
/// <remarks>
/// Positive assertion only, deliberately. See the invariant in <see cref="MySqlTestConnection"/>: a second
/// TLS-negotiating test, in a second collection, is the precondition for #1854 rather than a check against it.
/// </remarks>
[Collection(RuntimeCommandTransportMySqlContainerFixture.CollectionName)]
public sealed class MySqlCommandTransportConnectionTests(RuntimeCommandTransportMySqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Fixture_connection_string_does_not_negotiate_tls()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");

        Assert.Equal(string.Empty, await MySqlTestConnectionTests.SessionCipherAsync(fixture.ConnectionString));
    }
}

/// <summary>
/// Pins which MySQL fixtures this assembly has, because the two guards above are per-fixture: a third fixture added
/// later would negotiate TLS by default, restore the #1854 precondition, and pass both of them. Needs no container,
/// so it runs in every lane rather than only where Docker is.
/// </summary>
public sealed class MySqlFixtureInventoryTests
{
    [Fact]
    public void Every_mysql_fixture_in_this_assembly_is_guarded()
    {
        var fixtures = typeof(MySqlTestConnection).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } &&
                           type.Name.EndsWith("MySqlContainerFixture", StringComparison.Ordinal))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["RuntimeCommandTransportMySqlContainerFixture", "RuntimePlacementMySqlContainerFixture"],
            fixtures);
    }
}
