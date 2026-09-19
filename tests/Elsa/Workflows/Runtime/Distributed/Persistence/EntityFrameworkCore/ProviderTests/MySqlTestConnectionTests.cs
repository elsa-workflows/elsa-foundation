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

    private static async Task<string> SessionCipherAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW STATUS LIKE 'Ssl_cipher'";
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetString(1) : string.Empty;
    }
}
