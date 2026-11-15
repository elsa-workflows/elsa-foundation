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
/// a connection up front inside the fixtures would not help either: the two collection fixtures' own
/// <c>InitializeAsync</c> run in parallel, so the warm-up would race exactly as the first real connection did.
/// See #1854.
///
/// One invariant follows from this and is easy to break by accident: <b>at most one test in this assembly may
/// negotiate TLS, and it must live in a single collection.</b> <c>MySqlTestConnectionTests</c> is that test — its
/// negative control opens a deliberately TLS-negotiating connection to prove the guard is load-bearing. Mirroring
/// that control onto a second collection would put two negotiations back in parallel and reproduce #1854, so the
/// command-transport guard asserts the hardened string only.
/// </remarks>
internal static class MySqlTestConnection
{
    public static string WithoutSsl(string connectionString) =>
        new MySqlConnectionStringBuilder(connectionString) { SslMode = MySqlSslMode.Disabled }.ConnectionString;
}
