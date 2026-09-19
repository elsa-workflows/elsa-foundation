using MySql.Data.MySqlClient;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>Shapes the MySQL connection string this assembly's container fixtures hand out.</summary>
/// <remarks>
/// SSL is switched off deliberately, and must stay off. <c>MySql.Data</c> defaults <c>SslMode</c> to
/// <c>Preferred</c>, so it negotiates TLS even against a throwaway container on loopback, and
/// <c>MySql.Data.Common.Ssl</c> populates a plain <c>Dictionary</c> during that negotiation without synchronizing
/// it. This is the only test assembly in the repo with two MySQL collections, so xUnit starts both fixtures' first
/// connection at the same moment, both enter that negotiation together, and the dictionary is corrupted — surfacing
/// as "Operations that change non-concurrent collections must have exclusive access" out of
/// <c>EnsureCreatedAsync</c>, nowhere near the concurrency the failing test itself exercises.
///
/// Nothing in this assembly intends to exercise TLS, so the negotiation is removed rather than serialized. Warming
/// one fixture's connection would not warm the other's, and the two collections are not the only source of
/// concurrency either: one placement test opens two MySQL connections at once by design. See #1854.
/// </remarks>
internal static class MySqlTestConnection
{
    public static string WithoutSsl(string connectionString) =>
        new MySqlConnectionStringBuilder(connectionString) { SslMode = MySqlSslMode.Disabled }.ConnectionString;
}
