using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Public_v2_identity_survives_process_restart_and_rejects_duplicate_names(string provider)
    {
        var connectionEnvironmentVariable = ConnectionEnvironmentVariable(provider);
        var configured = connectionEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(connectionEnvironmentVariable);
        Skip.If(
            provider != "sqlite" && string.IsNullOrWhiteSpace(configured) && !IsCi(),
            $"Set {connectionEnvironmentVariable} locally, or run the matrix in CI.");
        await using var runtime = await ProviderRuntime.CreateAsync(provider, configured);
        var suffix = $"identity_{Guid.NewGuid():N}"[..17];
        var original = new IdentityProcessProbeUser(
            "tenant-process-restart",
            "user-original",
            "ada",
            "ADA",
            "ada@example.test",
            "ADA@EXAMPLE.TEST");
        var duplicate = new IdentityProcessProbeUser(
            original.TenantId,
            "user-duplicate",
            "ada-duplicate",
            original.NormalizedUserName,
            "ada-duplicate@example.test",
            "ADA-DUPLICATE@EXAMPLE.TEST");
        var state = new IdentityProcessProbeState(runtime.ConnectionString);
        var runner = new IdentityProcessProbeRunner();

        var created = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.CreateUser,
            original,
            state);
        var found = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.FindByNormalizedUserName,
            original,
            state);
        var rejected = await runner.RunAsync(
            provider,
            suffix,
            IdentityProcessProbeOperation.DuplicateCreate,
            duplicate,
            state);

        var originalIdDigest = IdentityProcessProbeProtocol.ComputeSha256(original.UserId);
        Assert.Equal("created", created.Outcome);
        Assert.Equal("found", found.Outcome);
        Assert.Equal("duplicate-rejected", rejected.Outcome);
        Assert.Equal("DuplicateUserName", rejected.ErrorCode);
        Assert.Equal(originalIdDigest, created.FoundUserIdSha256);
        Assert.Equal(originalIdDigest, found.FoundUserIdSha256);
        Assert.Equal(originalIdDigest, rejected.FoundUserIdSha256);
        Assert.Equal(1, created.DocumentVersion);
        Assert.Equal(1, found.DocumentVersion);
        Assert.Equal(1, rejected.DocumentVersion);
        Assert.Equal(3, new[] { created.ProcessId, found.ProcessId, rejected.ProcessId }.Distinct().Count());
    }

    [Fact]
    public void Process_protocol_never_renders_provider_state_or_identity_payloads()
    {
        const string connectionString = "Server=secret-host;Password=secret-password";
        var user = new IdentityProcessProbeUser(
            "secret-tenant",
            "secret-user",
            "secret-name",
            "SECRET-NAME",
            "secret@example.test",
            "SECRET@EXAMPLE.TEST");
        var command = new IdentityProcessProbeCommand(
            IdentityProcessProbeProtocol.CurrentVersion,
            new string('a', 64),
            "sqlserver",
            "identity_probe",
            IdentityProcessProbeOperation.CreateUser,
            user,
            new IdentityProcessProbeState(connectionString));

        Assert.DoesNotContain(connectionString, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.UserId, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.NormalizedUserName, user.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, command.State.ToString(), StringComparison.Ordinal);
    }

    private static bool IsCi() => Environment.GetEnvironmentVariable("CI") is "1" or "true";

    private static string? ConnectionEnvironmentVariable(string provider) => provider switch
    {
        "sqlite" => null,
        "postgresql" => "GROUNDWORK_POSTGRES_CONNECTION",
        "sqlserver" => "GROUNDWORK_SQLSERVER_CONNECTION",
        "mongodb" => "GROUNDWORK_MONGO_CONNECTION",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private sealed class ProviderRuntime(
        string connectionString,
        IAsyncDisposable? container,
        string? sqlitePath) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<ProviderRuntime> CreateAsync(string provider, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return new ProviderRuntime(configured, null, null);
            return provider switch
            {
                "sqlite" => CreateSqlite(),
                "postgresql" => await CreatePostgreSqlAsync(),
                "sqlserver" => await CreateSqlServerAsync(),
                "mongodb" => await CreateMongoDbAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is null)
                return;
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }

        private static ProviderRuntime CreateSqlite()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-identity-v2-{Guid.NewGuid():N}.db");
            return new ProviderRuntime($"Data Source={path}", null, path);
        }

        private static async Task<ProviderRuntime> CreatePostgreSqlAsync()
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            return new ProviderRuntime(container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateSqlServerAsync()
        {
            var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            return new ProviderRuntime(container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateMongoDbAsync()
        {
            var container = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
            await container.StartAsync();
            var connection = container.GetConnectionString();
            var queryStart = connection.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connection : connection[..queryStart]).TrimEnd('/');
            return new ProviderRuntime(
                $"{server}/elsa_identity_v2?replicaSet=rs0&authSource=admin&directConnection=true",
                container,
                null);
        }
    }
}
