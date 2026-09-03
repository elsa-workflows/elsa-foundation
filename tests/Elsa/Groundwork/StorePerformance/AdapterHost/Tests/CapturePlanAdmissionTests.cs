using System.Diagnostics;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class CapturePlanAdmissionTests
{
    [Theory]
    [InlineData("password", "super-secret")]
    [InlineData("server", "db.example.test")]
    public void Capture_plan_rejects_sensitive_request_fields_before_provider_or_output_access(
        string key,
        string value)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capture-plan-rejected-{Guid.NewGuid():N}");
        var request = Request() with
        {
            ProviderConfiguration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = value
            }
        };

        try
        {
            Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request, output));
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Capture_plan_rejects_malformed_request_at_the_admission_boundary()
    {
        var request = Request() with { NativePlanEvidenceReference = "../escape.json" };

        var exception = Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request, ExternalOutput()));

        Assert.Contains("actual frozen input", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_plan_binds_evidence_reference_to_the_measurement_set_before_provider_access()
    {
        var request = Request() with
        {
            NativePlanEvidenceReference = "checkpoint-commit.sqlite.other-set.native-plan.json"
        };

        var exception = Assert.Throws<PerformanceContractException>(() => CapturePlanAdmission.Ensure(request, ExternalOutput()));

        Assert.Contains("checkpoint-commit.sqlite.set.native-plan.json", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Groundwork.Store", "0.4.0-preview.9")]
    [InlineData("Groundwork.Sqlite", "0.4.0-preview.5")]
    public void Capture_plan_rejects_non_current_provider_package_provenance(string package, string version)
    {
        var request = Request() with
        {
            PackageVersions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [package] = version
            }
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            CapturePlanAdmission.Ensure(request, ExternalOutput()));

        Assert.Contains("must exactly match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_plan_admits_a_registered_workload_with_a_real_capture_implementation()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[RuntimeBookmarkLookupWorkload.WorkloadId];
        var request = Request() with
        {
            WorkloadId = workload.Id,
            WorkloadVersion = workload.Version,
            Adapter = BenchmarkAdapterRegistry.GroundworkV2Adapter,
            PhysicalForm = BookmarkLookupAdapter.PhysicalForm,
            Seed = workload.Input.Seed,
            InputFingerprintSha256 = workload.Input.FingerprintSha256,
            NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(workload.Id, "sqlite", "set")
        };

        var output = ExternalOutput();
        try
        {
            Assert.EndsWith(Path.GetFileName(output), CapturePlanAdmission.Ensure(request, output), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Theory]
    [InlineData("queue-drain", "dedicated-scheduler-work-documents")]
    [InlineData("command-send-lease-ack", "dedicated-command-transport-documents")]
    public void Capture_plan_rejects_mongo_runtime_distinct_routes_before_provider_or_output_access(
        string workloadId,
        string physicalForm)
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[workloadId];
        var output = ExternalOutput();
        var request = Request() with
        {
            WorkloadId = workload.Id,
            WorkloadVersion = workload.Version,
            Provider = "mongodb",
            ProviderVersion = "8.0.0",
            ProviderTopology = workload.RequiredProviderEvidence["mongodb"],
            ProviderConfiguration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = "candidate"
            },
            Adapter = BenchmarkAdapterRegistry.GroundworkV2Adapter,
            PhysicalForm = physicalForm,
            PackageVersions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Groundwork.MongoDb"] = "0.4.0-preview.9"
            },
            Seed = workload.Input.Seed,
            InputFingerprintSha256 = workload.Input.FingerprintSha256,
            NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(workload.Id, "mongodb", "set")
        };

        try
        {
            var exception = Assert.Throws<PerformanceContractException>(() =>
                CapturePlanAdmission.Ensure(request, output));

            Assert.Contains(BenchmarkAdapterRegistry.MongoRuntimeNativePlanBlockedReason, exception.Message, StringComparison.Ordinal);
            Assert.Contains("correctness path remains admitted", exception.Message, StringComparison.Ordinal);
            Assert.Contains("descriptive observer command", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Diagnostics_capture_plan_is_allowed_to_reach_provider_resolution_while_timing_stays_blocked()
    {
        var request = DiagnosticsRequest();

        CapturePlanAdmission.Ensure(request, ExternalOutput());
    }

    [Fact]
    public void Diagnostics_correctness_admission_resolves_the_provider_but_run_remains_blocked()
    {
        var request = DiagnosticsRequest();
        var args = new[]
        {
            "verify-correctness",
            "--request",
            RunRequestWire.Serialize(request),
            "--out",
            Path.Combine(Path.GetTempPath(), $"diagnostics-correctness-{Guid.NewGuid():N}")
        };
        var resolved = false;

        SecretRunAdmission.ParseAndResolve(args, "verify-correctness", _ =>
        {
            resolved = true;
            return "provider-connection";
        });

        Assert.True(resolved);
        var runArgs = (string[])args.Clone();
        runArgs[0] = "run";
        Assert.Throws<PerformanceContractException>(() =>
            SecretRunAdmission.ParseAndResolve(runArgs, "run", _ =>
            {
                throw new InvalidOperationException("The blocked run must not resolve a provider.");
            }));
    }

    [Fact]
    public void Evidence_bypass_does_not_admit_the_temporary_ef_comparator_on_non_sqlite()
    {
        var request = DiagnosticsRequest() with
        {
            Provider = "postgresql",
            ProviderVersion = "16.0",
            ProviderTopology = "real-postgresql-container",
            Adapter = BenchmarkAdapterRegistry.EfDiagnosticsAdapterId,
            PhysicalForm = EfDiagnosticsDurableHistoryAdapter.PhysicalForm
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateEvidenceRequest(
                WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[DiagnosticsDurableHistoryWorkload.WorkloadId],
                request));

        Assert.Contains(BenchmarkAdapterAdmission.DiagnosticsEfProviderRequiredReason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlserver", "real-sqlserver-container")]
    [InlineData("postgresql", "real-postgresql-container")]
    [InlineData("mongodb", "transaction-capable-replica-set")]
    public void Capture_plan_rejects_non_sqlite_ef_secret_before_provider_or_artifact_access(
        string provider,
        string topology)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capture-plan-secret-rejected-{Guid.NewGuid():N}");

        try
        {
            var exception = Assert.Throws<PerformanceContractException>(() =>
                CapturePlanAdmission.Ensure(SecretRequest(provider, topology), output));

            Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Theory]
    [InlineData("run", "sqlserver", "real-sqlserver-container")]
    [InlineData("run", "postgresql", "real-postgresql-container")]
    [InlineData("run", "mongodb", "transaction-capable-replica-set")]
    [InlineData("verify-correctness", "sqlserver", "real-sqlserver-container")]
    [InlineData("verify-correctness", "postgresql", "real-postgresql-container")]
    [InlineData("verify-correctness", "mongodb", "transaction-capable-replica-set")]
    public void Direct_commands_reject_non_sqlite_ef_secret_before_connection_environment_resolution(
        string command,
        string provider,
        string topology)
    {
        var request = SecretRequest(provider, topology);
        var connectionEnvironmentRead = false;
        var args = new[]
        {
            command,
            "--request",
            RunRequestWire.Serialize(request),
            "--out",
            Path.Combine(Path.GetTempPath(), $"secret-direct-rejected-{Guid.NewGuid():N}")
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            SecretRunAdmission.ParseAndResolve(
                args,
                command,
                _ =>
                {
                    connectionEnvironmentRead = true;
                    throw new InvalidOperationException("The connection environment must not be read.");
                }));

        Assert.Contains(BenchmarkAdapterAdmission.SecretEfProviderRequiredReason, exception.Message, StringComparison.Ordinal);
        Assert.False(connectionEnvironmentRead);
        Assert.False(Directory.Exists(args[^1]));
    }

    [Fact]
    public void Direct_commands_reject_repository_output_before_connection_resolution()
    {
        var repositoryRoot = SourceProvenance.FindRepositoryRoot();
        var args = new[]
        {
            "run",
            "--request",
            RunRequestWire.Serialize(Request()),
            "--out",
            Path.Combine(repositoryRoot, "artifacts")
        };
        var connectionEnvironmentRead = false;

        var exception = Assert.Throws<PerformanceContractException>(() =>
            SecretRunAdmission.ParseAndResolve(args, "run", _ =>
            {
                connectionEnvironmentRead = true;
                return "provider-connection";
            }));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(connectionEnvironmentRead);
    }

    [Fact]
    public void Direct_commands_reject_non_current_packages_before_connection_resolution()
    {
        var request = Request() with
        {
            PackageVersions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Groundwork.Sqlite"] = "0.4.0-preview.5"
            }
        };
        var args = new[]
        {
            "run",
            "--request",
            RunRequestWire.Serialize(request),
            "--out",
            ExternalOutput()
        };
        var connectionEnvironmentRead = false;

        var exception = Assert.Throws<PerformanceContractException>(() =>
            SecretRunAdmission.ParseAndResolve(args, "run", _ =>
            {
                connectionEnvironmentRead = true;
                return "provider-connection";
            }));

        Assert.Contains("must exactly match", exception.Message, StringComparison.Ordinal);
        Assert.False(connectionEnvironmentRead);
    }

    [Fact]
    public async Task Adapter_host_cli_rejects_repository_output_before_source_or_provider_access()
    {
        var (exitCode, standardError) = await RunHostAsync(
            "run",
            Request(),
            Path.Combine(SourceProvenance.FindRepositoryRoot(), "artifacts"));

        Assert.Equal(2, exitCode);
        Assert.Contains("outside", standardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clean repository", standardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adapter_host_capture_rejects_a_symlink_into_the_repository_before_source_or_provider_access()
    {
        if (OperatingSystem.IsWindows())
            return;

        var externalRoot = Path.Combine(Path.GetTempPath(), $"elsa646-capture-link-{Guid.NewGuid():N}");
        var link = Path.Combine(externalRoot, "repository-link");
        Directory.CreateDirectory(externalRoot);
        Directory.CreateSymbolicLink(link, SourceProvenance.FindRepositoryRoot());
        try
        {
            var (exitCode, standardError) = await RunHostAsync(
                "capture-plan",
                Request(),
                Path.Combine(link, "artifacts"));

            Assert.Equal(2, exitCode);
            Assert.Contains("outside", standardError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clean repository", standardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(externalRoot);
        }
    }

    [Fact]
    public void Output_admission_requires_a_tree_disjoint_from_the_repository()
    {
        var repositoryRoot = SourceProvenance.FindRepositoryRoot();
        var externalOutput = ExternalOutput();

        Assert.Throws<PerformanceContractException>(() =>
            ArtifactOutputAdmission.RequireExternal(repositoryRoot, repositoryRoot));
        Assert.Throws<PerformanceContractException>(() =>
            ArtifactOutputAdmission.RequireExternal(Path.Combine(repositoryRoot, "artifacts", "..", "artifacts"), repositoryRoot));
        Assert.Throws<PerformanceContractException>(() =>
            ArtifactOutputAdmission.RequireExternal(Path.GetDirectoryName(repositoryRoot)!, repositoryRoot));
        Assert.EndsWith(
            Path.GetFileName(externalOutput),
            ArtifactOutputAdmission.RequireExternal(externalOutput, repositoryRoot),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Output_admission_resolves_a_symlink_into_the_repository()
    {
        if (OperatingSystem.IsWindows())
            return;

        var externalRoot = Path.Combine(Path.GetTempPath(), $"elsa646-output-link-{Guid.NewGuid():N}");
        var link = Path.Combine(externalRoot, "repository-link");
        Directory.CreateDirectory(externalRoot);
        Directory.CreateSymbolicLink(link, SourceProvenance.FindRepositoryRoot());
        try
        {
            Assert.Throws<PerformanceContractException>(() =>
                ArtifactOutputAdmission.RequireExternal(Path.Combine(link, "artifacts"), SourceProvenance.FindRepositoryRoot()));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(externalRoot);
        }
    }

    private static string ExternalOutput() =>
        Path.Combine(Path.GetTempPath(), $"elsa646-output-{Guid.NewGuid():N}");

    private static async Task<(int ExitCode, string StandardError)> RunHostAsync(
        string command,
        RunRequest request,
        string outputDirectory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = SourceProvenance.FindRepositoryRoot()
        };
        start.ArgumentList.Add(typeof(MatrixCatalog).Assembly.Location);
        start.ArgumentList.Add(command);
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(RunRequestWire.Serialize(request));
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(outputDirectory);

        using var process = Process.Start(start)!;
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, standardError);
    }

    private static RunRequest Request() => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: "checkpoint-commit",
        WorkloadVersion: "1.1.0",
        Provider: "sqlite",
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "ReadWriteCreate"
        },
        Adapter: "groundwork-v2",
        PhysicalForm: "checkpoint-unit-of-work-with-linked-outbox",
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Sqlite"] = "0.4.0-preview.9"
        },
        Seed: "spec094-checkpoint-commit-v1.1",
        InputFingerprintSha256: "ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13",
        NativePlanIdentity: "checkpoint-commit-zero-routes",
        NativePlanEvidenceReference: "checkpoint-commit.sqlite.set.native-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 0);

    private static RunRequest SecretRequest(string provider, string topology) => new(
        ComparisonCohortId: "secret-cohort",
        MeasurementSetId: "secret-set",
        WorkloadId: SecretCreateReadListWorkload.WorkloadId,
        WorkloadVersion: SecretCreateReadListWorkload.Version,
        Provider: provider,
        ProviderVersion: "1.0.0",
        ProviderTopology: topology,
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "candidate"
        },
        Adapter: BenchmarkAdapterRegistry.EfSecretRepositoryAdapterId,
        PhysicalForm: EfSecretRepositoryAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.EntityFrameworkCore"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.10"
        },
        Seed: SecretCreateReadListWorkload.Seed,
        InputFingerprintSha256: SecretCreateReadListWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "secret-ef-plan",
        NativePlanEvidenceReference: NativePlanEvidenceStaging.ReferenceFor(
            SecretCreateReadListWorkload.WorkloadId,
            provider,
            "secret-set"),
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);

    private static RunRequest DiagnosticsRequest() => new(
        ComparisonCohortId: "diagnostics-cohort",
        MeasurementSetId: "diagnostics-set",
        WorkloadId: DiagnosticsDurableHistoryWorkload.WorkloadId,
        WorkloadVersion: DiagnosticsDurableHistoryWorkload.Version,
        Provider: "sqlite",
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "ReadWriteCreate"
        },
        Adapter: DiagnosticsDurableHistoryAdapter.AdapterId,
        PhysicalForm: DiagnosticsDurableHistoryAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Sqlite"] = "0.4.0-preview.9"
        },
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: DiagnosticsDurableHistoryWorkload.Seed,
        InputFingerprintSha256: DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "diagnostics-plan",
        NativePlanEvidenceReference: NativePlanEvidenceStaging.ReferenceFor(
            DiagnosticsDurableHistoryWorkload.WorkloadId,
            "sqlite",
            "diagnostics-set"),
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);
}
