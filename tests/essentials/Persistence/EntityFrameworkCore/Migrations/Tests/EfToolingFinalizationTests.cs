using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The persistence tool's <c>hold</c>, <c>release</c> and <c>status</c> (spec 181, FR-019, FR-020, FR-022 and SC-006),
/// through the host's own tooling entry point against a real database: a hold can be placed before any gate-aware
/// host has run, one on a finalized version is refused because the rollback boundary has been crossed, and no command
/// finalizes or lowers a finalized version. Every refusal is checked to have changed nothing.
/// </summary>
public sealed class EfToolingFinalizationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-tooling-finalization-{Guid.NewGuid():N}.db");

    private string Connection => $"Data Source={_file}";

    public async Task InitializeAsync() =>
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(new { version = 1, command = "apply", provider = "Sqlite", selection = Modules("Secrets"), connection = Connection })).ExitCode);

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_hold_is_placed_before_any_host_has_run_by_creating_the_record_and_names_its_reason_and_operator()
    {
        var hold = await HoldAsync(version: null);

        Assert.Equal(EfToolingExitCode.Success, hold.ExitCode);
        var family = Assert.Single(hold.Response.GetProperty("finalization").GetProperty("families").EnumerateArray());
        Assert.Equal((SecretsEfModule.SchemaFamily, SecretsEfModule.SchemaVersion), (family.GetProperty("family").GetString(), family.GetProperty("finalizedVersion").GetString()));
        var placed = Assert.Single(family.GetProperty("holds").EnumerateArray());
        Assert.Equal(("canary of the next release", "ops@example"), (placed.GetProperty("reason").GetString(), placed.GetProperty("placedBy").GetString()));

        var record = await RecordAsync();
        Assert.Equal([SchemaFinalizationTransition.Created, SchemaFinalizationTransition.HoldPlaced], record!.History.Select(entry => entry.Transition));
        Assert.Equal("ops@example", record.History[^1].Actor.Operator);
    }

    [Fact]
    public async Task A_hold_on_a_finalized_version_is_refused_because_the_rollback_boundary_has_been_crossed_and_changes_nothing()
    {
        await HoldAsync(version: null);
        await RunAsync(Release(version: null));
        var before = await RecordAsync();

        var refused = await HoldAsync(SecretsEfModule.SchemaVersion);

        Assert.Equal(EfToolingExitCode.Refusal, refused.ExitCode);
        Assert.Equal("rollback-boundary-crossed", refused.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("rollback boundary", refused.Response.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(before!.Revision, (await RecordAsync())!.Revision);
    }

    [Fact]
    public async Task A_released_hold_is_gone_and_releasing_it_again_is_refused()
    {
        await HoldAsync(version: null);

        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(Release(version: null))).ExitCode);
        Assert.Empty((await RecordAsync())!.Holds);
        var again = await RunAsync(Release(version: null));

        Assert.Equal(EfToolingExitCode.Refusal, again.ExitCode);
        Assert.Equal("no-hold", again.Response.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("operator")]
    public async Task A_hold_needs_a_reason_and_an_operator_and_writes_nothing_without_one(string missing)
    {
        var request = new
        {
            version = 1,
            command = "hold",
            provider = "Sqlite",
            selection = Modules("Secrets"),
            connection = Connection,
            finalization = new
            {
                family = SecretsEfModule.SchemaFamily,
                reason = missing == "reason" ? null : "canary",
                @operator = missing == "operator" ? null : "ops@example"
            }
        };

        var run = await RunAsync(request);

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
        Assert.Contains($"'finalization.{missing}' is required by 'hold'.", run.Response.GetProperty("error").GetProperty("details").EnumerateArray().Select(detail => detail.GetString()));
        Assert.Null(await RecordAsync());
    }

    [Fact]
    public async Task A_family_no_selected_module_owns_is_a_resolution_failure()
    {
        var run = await RunAsync(new
        {
            version = 1, command = "hold", provider = "Sqlite", selection = Modules("Secrets"), connection = Connection,
            finalization = new { family = "NoSuchFamily", reason = "canary", @operator = "ops" }
        });

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("unknown-family", run.Response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Status_reports_every_selected_family_whether_or_not_it_has_a_record()
    {
        await RunAsync(new { version = 1, command = "apply", provider = "Sqlite", selection = Modules("Workflows.Runtime"), connection = Connection });
        await HoldAsync(version: null);

        var status = await RunAsync(new { version = 1, command = "status", provider = "Sqlite", selection = Modules("Secrets", "Workflows.Runtime"), connection = Connection });

        Assert.Equal(EfToolingExitCode.Success, status.ExitCode);
        var families = status.Response.GetProperty("finalization").GetProperty("families").EnumerateArray().ToArray();
        Assert.Equal(13, families.Length);
        var secrets = families.Single(family => family.GetProperty("family").GetString() == SecretsEfModule.SchemaFamily);
        Assert.Single(secrets.GetProperty("holds").EnumerateArray());
        var runtime = families.Single(family => family.GetProperty("family").GetString() == RuntimeArtifactEfModule.SchemaFamily);
        Assert.Equal(JsonValueKind.Null, runtime.GetProperty("finalizedVersion").ValueKind);
        Assert.Equal([RuntimeArtifactEfModule.SchemaVersion], runtime.GetProperty("readableVersions").EnumerateArray().Select(version => version.GetString()));
    }

    [Fact]
    public async Task Status_refuses_the_fields_only_a_hold_takes()
    {
        var run = await RunAsync(new
        {
            version = 1, command = "status", provider = "Sqlite", selection = Modules("Secrets"), connection = Connection,
            finalization = new { family = SecretsEfModule.SchemaFamily, reason = "no", @operator = "ops" }
        });

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
    }

    private Task<Run> HoldAsync(string? version) => RunAsync(new
    {
        version = 1,
        command = "hold",
        provider = "Sqlite",
        selection = Modules("Secrets"),
        connection = Connection,
        finalization = new { family = SecretsEfModule.SchemaFamily, version, reason = "canary of the next release", @operator = "ops@example" }
    });

    private object Release(string? version) => new
    {
        version = 1,
        command = "release",
        provider = "Sqlite",
        selection = Modules("Secrets"),
        connection = Connection,
        finalization = new { family = SecretsEfModule.SchemaFamily, version, @operator = "ops@example" }
    };

    private static object Modules(params string[] modules) => new { kind = "modules", modules };

    private async Task<SchemaFinalizationRecord?> RecordAsync()
    {
        await using var context = ModuleContextCatalog.Create(typeof(SecretsSqliteDbContext), Connection);
        return await new EfSchemaFinalizationStore(context).FindAsync(SecretsEfModule.SchemaFamily);
    }

    private static async Task<Run> RunAsync(object request)
    {
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, Json));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, ModuleContextCatalog.Modules);
        using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
        return new Run(exitCode, response.RootElement.Clone());
    }

    private sealed record Run(int ExitCode, JsonElement Response);
}
