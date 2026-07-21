using System.Security.Cryptography;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// Spec 093 T061 (Half A): pins the Groundwork schema-tool CLI CONTRACT against the shipped
/// all-features deployment schema, executed exactly as a deployment would — the repository-pinned
/// <c>groundwork</c> tool in a child process loading a public, parameterless manifest source. This
/// lives in the SQLite leaf (rather than the provider-neutral shared assembly) because the
/// manifest-assembly reference chain the tool loads is provider-flavored; it reuses the leaf's
/// internal <see cref="GroundworkSchemaCli"/> helper (same assembly, same env-var / connection
/// convention, <c>ELSA_DESIGN_GROUNDWORK_SQLITE_CONNECTION</c>).
///
/// The exit-code contract pinned here (discovered empirically against tool preview.78) is:
///   * validate --offline (no connection)  -> exit 0, outcome "ready"       (schema well-formedness only)
///   * validate (live, unapplied target)   -> exit 0, outcome "ready"       (surfaces pendingOperations; never gates on applied state)
///   * plan   (unapplied target)           -> exit 2, outcome "pending"
///   * status (unapplied target)           -> exit 2, outcome "pending"
///   * apply --safe (unapplied target)     -> exit 0, outcome "applied", targetMutated=true
///   * apply --safe (already applied)      -> exit 0, outcome "ready",   targetMutated=false (idempotent)
///   * plan/status/validate (applied)      -> exit 0, outcome "ready",   pendingOperations empty
/// </summary>
public sealed class UnifiedSchemaToolContractTests : IDisposable
{
    private const int Success = 0;
    private const int PendingChanges = 2;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"elsa-unified-schema-tool-{Guid.NewGuid():N}");

    public UnifiedSchemaToolContractTests() => Directory.CreateDirectory(_directory);

    private string DatabasePath => Path.Combine(_directory, "design.db");
    private string ConnectionString => $"Data Source={DatabasePath}";

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named directory is harmless if SQLite releases a sidecar late.
        }
    }

    [Fact]
    public async Task Offline_validate_is_deterministic_and_never_touches_a_target()
    {
        // Offline validation carries no connection, so it can never create or mutate a database.
        var first = await GroundworkSchemaCli.RunAsync("validate", connectionString: null, CancellationToken.None, "--offline");
        var second = await GroundworkSchemaCli.RunAsync("validate", connectionString: null, CancellationToken.None, "--offline");

        Assert.Equal(Success, first.ExitCode);
        Assert.Equal(Success, second.ExitCode);
        Assert.Equal("ready", first.Report.GetProperty("outcome").GetString());
        Assert.Equal("ready", second.Report.GetProperty("outcome").GetString());

        var firstFingerprint = TargetFingerprint(first);
        Assert.False(string.IsNullOrWhiteSpace(firstFingerprint));
        Assert.Equal(firstFingerprint, TargetFingerprint(second));

        Assert.Equal("absent", DatabaseFingerprint(DatabasePath));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Plan_on_an_unapplied_target_is_pending_deterministic_and_non_mutating()
    {
        var before = DatabaseFingerprint(DatabasePath);

        var first = await GroundworkSchemaCli.RunAsync("plan", ConnectionString, CancellationToken.None);
        var second = await GroundworkSchemaCli.RunAsync("plan", ConnectionString, CancellationToken.None);

        Assert.Equal(PendingChanges, first.ExitCode);
        Assert.Equal(PendingChanges, second.ExitCode);
        Assert.Equal("pending", first.Report.GetProperty("outcome").GetString());
        Assert.True(first.Report.GetProperty("pendingOperations").GetArrayLength() > 0);
        Assert.False(first.Report.GetProperty("targetMutated").GetBoolean());

        // Determinism: the plan fingerprint is identical across independent runs.
        Assert.Equal(PlanFingerprint(first), PlanFingerprint(second));

        // Mutation-free: plan never provisions the target. A fresh target has no file before or after.
        Assert.Equal(before, DatabaseFingerprint(DatabasePath));
        Assert.Equal("absent", DatabaseFingerprint(DatabasePath));
        Assert.False(File.Exists(DatabasePath + "-wal"));
    }

    [Fact]
    public async Task Status_and_validate_pin_their_not_ready_exit_codes_without_mutating()
    {
        var status = await GroundworkSchemaCli.RunAsync("status", ConnectionString, CancellationToken.None);
        var validate = await GroundworkSchemaCli.RunAsync("validate", ConnectionString, CancellationToken.None);

        // status gates on applied state: not-ready is exit 2 / outcome "pending".
        Assert.Equal(PendingChanges, status.ExitCode);
        Assert.Equal("pending", status.Report.GetProperty("outcome").GetString());
        Assert.True(status.Report.GetProperty("pendingOperations").GetArrayLength() > 0);

        // validate gates on schema well-formedness only: exit 0 / outcome "ready", with the pending
        // operations surfaced rather than treated as a failure. This is the pinned distinction.
        Assert.Equal(Success, validate.ExitCode);
        Assert.Equal("ready", validate.Report.GetProperty("outcome").GetString());
        Assert.True(validate.Report.GetProperty("pendingOperations").GetArrayLength() > 0);

        Assert.False(status.Report.GetProperty("targetMutated").GetBoolean());
        Assert.False(validate.Report.GetProperty("targetMutated").GetBoolean());
        Assert.Equal("absent", DatabaseFingerprint(DatabasePath));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Safe_apply_applies_once_and_is_idempotent_then_validates_ready()
    {
        var plan = await GroundworkSchemaCli.RunAsync("plan", ConnectionString, CancellationToken.None);
        var pendingCount = plan.Report.GetProperty("pendingOperations").GetArrayLength();
        Assert.True(pendingCount > 0);

        var firstApply = await GroundworkSchemaCli.RunAsync("apply", ConnectionString, CancellationToken.None, "--safe");
        Assert.Equal(Success, firstApply.ExitCode);
        Assert.Equal("applied", firstApply.Report.GetProperty("outcome").GetString());
        Assert.True(firstApply.Report.GetProperty("targetMutated").GetBoolean());
        Assert.Equal(pendingCount, firstApply.Report.GetProperty("appliedOperations").GetArrayLength());
        Assert.Equal(0, firstApply.Report.GetProperty("pendingOperations").GetArrayLength());
        Assert.True(File.Exists(DatabasePath));

        // A second safe apply is idempotent: nothing is newly applied and the target is not mutated.
        var secondApply = await GroundworkSchemaCli.RunAsync("apply", ConnectionString, CancellationToken.None, "--safe");
        Assert.Equal(Success, secondApply.ExitCode);
        Assert.Equal("ready", secondApply.Report.GetProperty("outcome").GetString());
        Assert.False(secondApply.Report.GetProperty("targetMutated").GetBoolean());
        Assert.Equal(0, secondApply.Report.GetProperty("pendingOperations").GetArrayLength());

        var validate = await GroundworkSchemaCli.RunAsync("validate", ConnectionString, CancellationToken.None);
        Assert.Equal(Success, validate.ExitCode);
        Assert.Equal("ready", validate.Report.GetProperty("outcome").GetString());
        Assert.Equal(0, validate.Report.GetProperty("pendingOperations").GetArrayLength());
    }

    [Fact]
    public async Task Plan_after_apply_reports_nothing_pending_and_does_not_mutate()
    {
        var apply = await GroundworkSchemaCli.RunAsync("apply", ConnectionString, CancellationToken.None, "--safe");
        Assert.Equal(Success, apply.ExitCode);
        Assert.Equal("applied", apply.Report.GetProperty("outcome").GetString());

        var before = DatabaseFingerprint(DatabasePath);

        var plan = await GroundworkSchemaCli.RunAsync("plan", ConnectionString, CancellationToken.None);
        Assert.Equal(Success, plan.ExitCode);
        Assert.Equal("ready", plan.Report.GetProperty("outcome").GetString());
        Assert.Equal(0, plan.Report.GetProperty("pendingOperations").GetArrayLength());
        Assert.False(plan.Report.GetProperty("targetMutated").GetBoolean());

        // The durable main-file bytes are unchanged by plan (see DatabaseFingerprint for the
        // deliberate sidecar-exclusion rationale).
        Assert.Equal(before, DatabaseFingerprint(DatabasePath));
    }

    private static string? TargetFingerprint(GroundworkCliResult result) =>
        result.Report.GetProperty("target").GetProperty("fingerprint").GetString();

    private static string? PlanFingerprint(GroundworkCliResult result) =>
        result.Report.GetProperty("planFingerprint").GetString();

    /// <summary>
    /// Fingerprints only the main SQLite database file. The -wal/-shm/-lock sidecars are transient
    /// journal and coordination artifacts that the tool creates and empties per inspection
    /// connection — an empty -wal appears after a post-apply plan even though nothing durable
    /// changed — so folding them into the digest would produce false mutation positives. The main
    /// file carries the durable applied schema bytes. Absence is a stable sentinel.
    /// </summary>
    private static string DatabaseFingerprint(string databasePath) =>
        File.Exists(databasePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(databasePath)))
            : "absent";
}
