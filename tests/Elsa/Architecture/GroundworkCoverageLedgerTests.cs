using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkCoverageLedgerTests
{
    private const string EntryId = "runtime-activity-execution-inspection";
    private const string ExpectedGroundworkVersion = "0.0.1-preview.103";
    private const string CurrentV2GroundworkVersion = "0.4.0-preview.1";
    private const string Prior88GroundworkVersion = "0.0.1-preview.88";
    private const string Prior86GroundworkVersion = "0.0.1-preview.86";
    private const string Prior81GroundworkVersion = "0.0.1-preview.81";
    private const string HistoricalGroundworkVersion = "0.0.1-preview.80";
    private const string ImmutableActivationLedgerRef = "dec0b88bc21db15aa3c22181648ab201c483b01a";
    private const string LedgerRelativePath = "specs/094-harden-groundwork-stores/coverage-ledger.json";
    private const string Prior88CheckpointFenceAttachmentRelativePath =
        "specs/094-harden-groundwork-stores/versions/0.0.1-preview.88/ledger-attachments/runtime-checkpoint-fence.json";
    private const string Prior88CheckpointFenceAttachmentSha256 =
        "f0b40406e1e5a044bb8e83e6090c3eb84b676124674cd948ed2440f227b065f2";
    private const string Prior86CheckpointFenceAttachmentRelativePath =
        "specs/094-harden-groundwork-stores/versions/0.0.1-preview.86/ledger-attachments/runtime-checkpoint-fence.json";
    private const string Prior86CheckpointFenceAttachmentSha256 =
        "954a34a1bb3ce03881bedd167ba87c95d7d58d3f5abdb573e50e123361e0ef24";
    private const string Prior81CheckpointFenceAttachmentRelativePath =
        "specs/094-harden-groundwork-stores/versions/0.0.1-preview.81/ledger-attachments/runtime-checkpoint-fence.json";
    private const string Prior81CheckpointFenceAttachmentSha256 =
        "ee6ea1c85dad6d1506abfbb7899ca73b33f52ae811fd35e254b0f9bce36ddf34";
    private const string HistoricalCheckpointFenceAttachmentRelativePath =
        "specs/094-harden-groundwork-stores/ledger-attachments/runtime-checkpoint-fence.json";
    private const string HistoricalCheckpointFenceAttachmentSha256 =
        "b8fb7ce1faea246d3746c0c586b4e870d0309f17d84490e19a93b957600fac7c";
    private const string Prior81CheckpointFenceEvidenceCommit = "bf452355867c8f76a11d9bca9191563a773a631a";
    private const string Prior81CheckpointFenceEvidenceTree = "8b3504d52cef5f4a19ae5318fc66f46aefcfd048";
    private const string Prior81CheckpointFenceRunIdentity = "runtime-checkpoint-fence-preview81";
    private const string Prior86CheckpointFenceEvidenceCommit = "2dc442ea31061971cae6a86a8e8f0a13904cbeb7";
    private const string Prior86CheckpointFenceEvidenceTree = "ae590a5d927e83b9688afa878a02214ed81ee9e9";
    private const string Prior86CheckpointFenceRunIdentity = "runtime-checkpoint-fence-preview86";
    private const string Prior88CheckpointFenceEvidenceCommit = "b0545e166fd45aa872f265c88782a7034a09c357";
    private const string Prior88CheckpointFenceEvidenceTree = "613afd96195b4ef28546a67f099d259e5ffbe448";
    private const string Prior88CheckpointFenceRunIdentity = "runtime-checkpoint-fence-preview88";
    private const string Current103CheckpointFenceAttachmentRelativePath =
        "specs/094-harden-groundwork-stores/versions/0.0.1-preview.103/ledger-attachments/runtime-checkpoint-fence.json";
    private const string Current103CheckpointFenceAttachmentSha256 =
        "61eed043b056a1c40bb5ff5bd2a48f7bc7eb04a363cb1788cf4403538f11303e";
    private const string Current103CheckpointFenceEvidenceCommit = "78e64899684e61f7c02e9273829c16c014ddc446";
    private const string Current103CheckpointFenceEvidenceTree = "17acb4c7a3c90bddac7b89ef535b35aa714edec1";
    private const string Current103CheckpointFenceRunIdentity = "runtime-checkpoint-fence-preview103";

    private static readonly string[] ExpectedEntryIds =
    [
        "diagnostics-open-telemetry-store",
        "diagnostics-structured-log-store",
        "runtime-activity-execution-inspection",
        "runtime-activity-execution-state",
        "runtime-bookmark-state",
        "runtime-durable-timer",
        "runtime-durable-value-state",
        "runtime-execution-liveness",
        "runtime-incident-state",
        "runtime-recurring-trigger-schedule",
        "runtime-checkpoint-commit",
        "runtime-diagnostics-settings",
        "runtime-post-commit-outbox",
        "runtime-scheduler-state",
        "runtime-executable-source-reference",
        "runtime-workflow-executable",
        "runtime-workflow-execution-state",
        "runtime-workflow-alteration",
        "runtime-workflow-hold-state",
        "runtime-scheduler-poison",
        "runtime-scheduler-work-queue",
        "runtime-trigger-binding",
        "runtime-publication-projection-state",
        "iam-user",
        "iam-role",
        "iam-application",
        "iam-credential",
        "iam-external-identity",
        "iam-claim-mapping",
        "iam-provider-configuration-tenant",
        "iam-provider-configuration-global",
        "iam-tenant-membership",
        "secrets-repository",
        "distributed-execution-placement",
        "distributed-command-transport"
    ];

    [Fact]
    public void Checked_in_ledger_conforms_to_its_schema_and_preserves_the_additive_35_row_denominator()
    {
        var ledger = ReadLedger();

        var validator = CreateEvidenceValidator();
        var findings = validator.Validate(ledger)
            .Concat(validator.ValidateImmutableBaseline(ReadImmutableActivationLedger(), ledger))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualEntryIds = Entries(ledger)
            .Select(EntryIdOf)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(findings);
        Assert.Equal(35, actualEntryIds.Length);
        Assert.Equal(ExpectedEntryIds.Order(StringComparer.Ordinal), actualEntryIds);
    }

    [Fact]
    public void Preview81_checkpoint_fence_attachment_remains_imported_exactly_once_as_prior_provenance()
    {
        AssertCheckpointFenceGeneration(
            Prior81GroundworkVersion,
            Prior81CheckpointFenceAttachmentRelativePath,
            Prior81CheckpointFenceAttachmentSha256,
            Prior81CheckpointFenceEvidenceCommit,
            Prior81CheckpointFenceEvidenceTree,
            Prior81CheckpointFenceRunIdentity);
    }

    [Fact]
    public void Preview86_checkpoint_fence_attachment_remains_imported_exactly_once_as_prior_provenance()
    {
        AssertCheckpointFenceGeneration(
            Prior86GroundworkVersion,
            Prior86CheckpointFenceAttachmentRelativePath,
            Prior86CheckpointFenceAttachmentSha256,
            Prior86CheckpointFenceEvidenceCommit,
            Prior86CheckpointFenceEvidenceTree,
            Prior86CheckpointFenceRunIdentity);
    }

    [Fact]
    public void Preview88_checkpoint_fence_attachment_remains_imported_exactly_once_as_prior_provenance()
    {
        AssertCheckpointFenceGeneration(
            Prior88GroundworkVersion,
            Prior88CheckpointFenceAttachmentRelativePath,
            Prior88CheckpointFenceAttachmentSha256,
            Prior88CheckpointFenceEvidenceCommit,
            Prior88CheckpointFenceEvidenceTree,
            Prior88CheckpointFenceRunIdentity);
    }

    [Fact]
    public void Preview103_checkpoint_fence_attachment_remains_imported_exactly_once_as_prior_provenance()
    {
        AssertCheckpointFenceGeneration(
            ExpectedGroundworkVersion,
            Current103CheckpointFenceAttachmentRelativePath,
            Current103CheckpointFenceAttachmentSha256,
            Current103CheckpointFenceEvidenceCommit,
            Current103CheckpointFenceEvidenceTree,
            Current103CheckpointFenceRunIdentity);
    }

    /// <summary>
    /// The recorded provenance commit must stay reachable from the branch. An earlier import recorded a
    /// commit that a rebase then rewrote away, which left the retained generation naming a source snapshot
    /// that existed nowhere -- the exact property the generation is retained to provide. Every prior
    /// generation records a commit reachable from main; this keeps that true by construction.
    /// </summary>
    [Fact]
    public void Preview103_provenance_commit_is_reachable_from_the_current_branch()
    {
        var result = RunGit($"merge-base --is-ancestor {Current103CheckpointFenceEvidenceCommit} HEAD");

        Assert.True(
            result == 0,
            $"Recorded provenance commit {Current103CheckpointFenceEvidenceCommit} is not an ancestor of HEAD; " +
            "the retained evidence names a source snapshot that cannot be checked out.");
    }

    /// <summary>
    /// Publication does not advance a row status, so importing the current generation must leave every row
    /// below <c>evidence-complete</c>. This half of the superseded
    /// <c>Preview103_checkpoint_fence_evidence_awaits_mechanical_import</c> fact is still live and is kept
    /// separate: a row only becomes evidence-complete when every declared obligation is present for all
    /// four providers, which the checkpoint/fence slice alone does not supply.
    /// </summary>
    [Fact]
    public void Importing_a_generation_does_not_advance_any_row_status()
    {
        Assert.DoesNotContain(
            Entries(ReadLedger()),
            entry => entry["status"]?.GetValue<string>() is
                "evidence-complete" or "performance-complete" or "ready");
    }

    private static int RunGit(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    [Fact]
    public void Preview88_exact_attachment_guard_allows_valid_later_evidence_outside_its_closed_slice()
    {
        var ledger = ReadLedger();
        var record = EvidenceRecord(
            "runtime-checkpoint-commit",
            "sqlite",
            "concurrencySemantic:expected-version-state-changes");
        record["concurrencySemantic"] = "expected-version-state-changes";
        WriteEvidenceArtifacts([record]);
        Entry(ledger, "runtime-checkpoint-commit")["providerEvidence"]!["sqlite"]!.AsArray().Add(record);

        Assert.Empty(CreateEvidenceValidator().Validate(ledger));
        AssertCheckpointFenceGeneration(
            Prior88GroundworkVersion,
            Prior88CheckpointFenceAttachmentRelativePath,
            Prior88CheckpointFenceAttachmentSha256,
            Prior88CheckpointFenceEvidenceCommit,
            Prior88CheckpointFenceEvidenceTree,
            Prior88CheckpointFenceRunIdentity,
            ledger);
    }

    [Fact]
    public void Retained_current_checkpoint_fence_generation_rejects_a_partial_import()
    {
        var ledger = ReadLedger();
        RemoveEvidenceGeneration(ledger, ExpectedGroundworkVersion);
        var record = EvidenceRecord(
            "runtime-checkpoint-commit",
            "sqlite",
            "concurrencySemantic:atomic-stale-fence-rejection");
        record["sourceScenarioId"] = "runtime-execution-ownership-fencing";
        record["concurrencySemantic"] = "atomic-stale-fence-rejection";
        record["provenance"] = new JsonObject
        {
            ["elsaCommit"] = new string('a', 40),
            ["elsaTree"] = new string('b', 40),
            ["runIdentity"] = "runtime-checkpoint-fence-preview103"
        };
        WriteEvidenceArtifacts([record]);
        Entry(ledger, "runtime-checkpoint-commit")["providerEvidence"]!["sqlite"]!.AsArray().Add(record);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"retained checkpoint/fence generation '{ExpectedGroundworkVersion}' must contain exactly 36 records; found 1.",
            findings);
    }

    [Fact]
    public void Retained_checkpoint_fence_generation_allows_valid_current_evidence_outside_its_closed_slice()
    {
        var ledger = ReadLedger();
        RemoveEvidenceGeneration(ledger, ExpectedGroundworkVersion);
        var record = EvidenceRecord(
            "runtime-checkpoint-commit",
            "sqlite",
            "concurrencySemantic:expected-version-state-changes");
        record["concurrencySemantic"] = "expected-version-state-changes";
        WriteEvidenceArtifacts([record]);
        Entry(ledger, "runtime-checkpoint-commit")["providerEvidence"]!["sqlite"]!.AsArray().Add(record);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Empty(findings);
    }

    [Fact]
    public void Preview80_checkpoint_fence_attachment_and_artifacts_remain_immutable_historical_provenance()
    {
        var attachmentPath = Path.Combine(
            RepoRoot,
            HistoricalCheckpointFenceAttachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var records = JsonNode.Parse(File.ReadAllText(attachmentPath))?.AsArray()
                          ?.OfType<JsonObject>()
                          .ToArray()
                      ?? throw new InvalidOperationException(
                          $"Historical checkpoint/fence attachment '{attachmentPath}' is empty.");

        Assert.Equal(
            HistoricalCheckpointFenceAttachmentSha256,
            GroundworkEvidenceArtifactContract.FileSha256(attachmentPath));
        Assert.Equal(36, records.Length);
        Assert.All(records, record =>
        {
            Assert.Equal(HistoricalGroundworkVersion, record["providerVersion"]?.GetValue<string>());
            var evidencePath = Path.Combine(
                RepoRoot,
                "specs/094-harden-groundwork-stores",
                record["evidence"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(
                record["evidenceSha256"]!.GetValue<string>(),
                GroundworkEvidenceArtifactContract.FileSha256(evidencePath));
            var artifact = JsonNode.Parse(File.ReadAllText(evidencePath))!.AsObject();
            Assert.Equal(HistoricalGroundworkVersion, artifact["providerVersion"]?.GetValue<string>());
        });
    }

    [Fact]
    public void Typed_loader_materializes_the_validated_checked_in_ledger()
    {
        var ledger = CreateValidator().Load(LedgerPath);

        Assert.Equal(1, ledger.SchemaVersion);
        Assert.Equal("094-harden-groundwork-stores", ledger.Feature);
        Assert.Equal(ExpectedGroundworkVersion, ledger.GroundworkVersion);
        Assert.Equal(ExpectedEntryIds, ledger.Entries.Select(entry => entry.Id));
        Assert.Equal(["sqlite", "sqlserver", "postgresql", "mongodb"], ledger.MandatoryProviders);
        Assert.Equal("host-selection-all35", ledger.CompositionEvidence.EvidenceId);
        Assert.Equal(ExpectedEntryIds, ledger.CompositionEvidence.CoveredEntryIds);
        Assert.Equal(8, ledger.CompositionEvidence.SelectedFeatureIdentities.Count);
    }

    [Fact]
    public void Composition_evidence_covers_all_35_rows_once_and_preserves_external_authority_links()
    {
        var ledger = ReadLedger();
        var evidence = ledger["compositionEvidence"]!.AsObject();
        var coveredRows = evidence["coveredEntryIds"]!.AsArray()
            .Select(row => row!.GetValue<string>())
            .ToArray();

        Assert.Equal(35, coveredRows.Length);
        Assert.Equal(35, coveredRows.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ExpectedEntryIds, coveredRows);

        var links = evidence["externalAuthorityLinks"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.Equal(
            ["iam-external-identity", "iam-role", "iam-user"],
            Assert.Single(links, link => link["authority"]!.GetValue<string>() == "#644")["coverageRows"]!
                .AsArray().Select(row => row!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["runtime-diagnostics-settings"],
            Assert.Single(links, link => link["authority"]!.GetValue<string>() == "#660")["coverageRows"]!
                .AsArray().Select(row => row!.GetValue<string>()));
        Assert.Equal("#644", Entry(ledger, "iam-user")["authority"]!.GetValue<string>());
        Assert.Equal("#644", Entry(ledger, "iam-role")["authority"]!.GetValue<string>());
        Assert.Equal("#644", Entry(ledger, "iam-external-identity")["authority"]!.GetValue<string>());
        Assert.Equal("#660", Entry(ledger, "runtime-diagnostics-settings")["authority"]!.GetValue<string>());
        Assert.Equal("#642", Entry(ledger, "diagnostics-open-telemetry-store")["authority"]!.GetValue<string>());
        Assert.Equal("#642", Entry(ledger, "diagnostics-structured-log-store")["authority"]!.GetValue<string>());
    }

    [Fact]
    public void Composition_evidence_rejects_missing_rows_and_artifact_digest_drift()
    {
        var ledger = ReadLedger();
        var evidence = ledger["compositionEvidence"]!.AsObject();
        evidence["coveredEntryIds"]!.AsArray().RemoveAt(0);
        evidence["artifactSha256"] = new string('0', 64);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            "composition evidence: coverage row 'diagnostics-open-telemetry-store' is missing from the reviewed host selection.",
            findings);
        Assert.Contains(
            "composition evidence: artifact 'evidence/composition/host-selection-all35.json' digest does not match its contents.",
            findings);
    }

    [Fact]
    public void Composition_evidence_rejects_external_authority_relationship_drift()
    {
        var ledger = ReadLedger();
        var evidence = ledger["compositionEvidence"]!.AsObject();
        var links = evidence["externalAuthorityLinks"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.Single(links, link => link["authority"]!.GetValue<string>() == "#644")["relationship"] = "linked-source-evidence";
        Assert.Single(links, link => link["authority"]!.GetValue<string>() == "#660")["relationship"] = "adapter-only";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            "composition evidence: external authority '#644' must use relationship 'adapter-only', not 'linked-source-evidence'.",
            findings);
        Assert.Contains(
            "composition evidence: external authority '#660' must use relationship 'linked-source-evidence', not 'adapter-only'.",
            findings);
    }

    /// <summary>
    /// Every Groundwork package is on the v2 line. The provider drivers share their package ids with v1 and
    /// this repo pins transitively, so a single id pulled back to the v1 line would put two incompatible
    /// drivers in one graph.
    /// </summary>
    [Fact]
    public void Pinned_Groundwork_packages_match_the_current_takeover_version()
    {
        var packageVersions = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(element => element.Attribute("Include")?.Value.StartsWith("Groundwork.", StringComparison.Ordinal) == true)
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "Groundwork.Kernel",
                "Groundwork.MongoDb",
                "Groundwork.PostgreSql",
                "Groundwork.Query.Model",
                "Groundwork.SqlServer",
                "Groundwork.Sqlite",
                "Groundwork.Store"
            },
            packageVersions.Keys.Order(StringComparer.Ordinal));
        Assert.All(packageVersions, pair => Assert.Equal(CurrentV2GroundworkVersion, pair.Value));
    }

    [Fact]
    public void Explicit_Groundwork_package_and_documentation_versions_match_the_reviewed_release()
    {
        var findings = new List<string>();
        foreach (var project in Directory.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedPath(path)))
        {
            var relativePath = Path.GetRelativePath(RepoRoot, project).Replace(Path.DirectorySeparatorChar, '/');
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants().Where(element =>
                         element.Name.LocalName == "PackageReference" &&
                         element.Attribute("Include")?.Value.StartsWith("Groundwork.", StringComparison.Ordinal) == true))
            {
                var version = reference.Attribute("VersionOverride")?.Value ?? reference.Attribute("Version")?.Value;
                if (version is null || version.StartsWith("$(", StringComparison.Ordinal))
                    continue;

                if (!string.Equals(version, CurrentV2GroundworkVersion, StringComparison.Ordinal))
                    findings.Add($"{relativePath}: explicit {reference.Attribute("Include")!.Value} version is '{version}', expected '{CurrentV2GroundworkVersion}'.");
            }

            foreach (var versionProperty in document.Descendants().Where(element =>
                         element.Name.LocalName == "GroundworkVersion" &&
                         !element.Value.StartsWith("$(", StringComparison.Ordinal)))
            {
                if (!string.Equals(versionProperty.Value, CurrentV2GroundworkVersion, StringComparison.Ordinal))
                    findings.Add($"{relativePath}: GroundworkVersion is '{versionProperty.Value}', expected '{CurrentV2GroundworkVersion}'.");
            }
        }

        var retiredV2Version = "0" + ".1.0-preview.1";
        string[] textExtensions = [".cs", ".csproj", ".json", ".md", ".props", ".ps1", ".sh", ".targets", ".yml", ".yaml"];
        foreach (var file in Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedPath(path) && textExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            if (File.ReadAllText(file).Contains(retiredV2Version, StringComparison.Ordinal))
                findings.Add($"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: still names retired v2 package '{retiredV2Version}'.");
        }

        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Current_source_test_and_benchmark_code_has_no_diagnostic_records_dependency()
    {
        var retiredSurface = string.Concat("Groundwork.", "DiagnosticRecords");
        var findings = new List<string>();
        foreach (var root in new[] { "src", "tests", "benchmarks" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*", SearchOption.AllDirectories)
                         .Where(path => !IsGeneratedPath(path) &&
                                        Path.GetExtension(path) is ".cs" or ".csproj"))
            {
                if (File.ReadAllText(file).Contains(retiredSurface, StringComparison.Ordinal))
                    findings.Add(Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        Assert.True(
            findings.Count == 0,
            $"Current code still references the retired diagnostic-record surface:{Environment.NewLine}{string.Join(Environment.NewLine, findings.Order(StringComparer.Ordinal))}");
    }

    [Fact]
    public void Checked_in_v2_consumer_restores_Groundwork_from_Feedz()
    {
        var config = XDocument.Load(Path.Combine(
            RepoRoot,
            "tests",
            "Elsa",
            "Diagnostics",
            "Persistence",
            "Groundwork",
            "V2",
            "Consumer",
            "NuGet.Config"));
        var sources = config.Descendants()
            .Where(element => element.Name.LocalName == "packageSources")
            .Elements()
            .Where(element => element.Name.LocalName == "add")
            .ToDictionary(
                element => element.Attribute("key")!.Value,
                element => element.Attribute("value")!.Value,
                StringComparer.Ordinal);
        var mappings = config.Descendants()
            .Where(element => element.Name.LocalName == "packageSource")
            .ToDictionary(
                element => element.Attribute("key")!.Value,
                element => element.Elements()
                    .Where(child => child.Name.LocalName == "package")
                    .Select(child => child.Attribute("pattern")!.Value)
                    .ToArray(),
                StringComparer.Ordinal);

        Assert.Equal(
            "https://f.feedz.io/valence-works/groundwork/nuget/index.json",
            sources["groundwork-feedz"]);
        Assert.Equal(["Groundwork.*"], mappings["groundwork-feedz"]);
        Assert.DoesNotContain(sources.Values, source =>
            source.StartsWith("./", StringComparison.Ordinal) ||
            source.StartsWith("../", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_schema_rejects_properties_that_are_not_declared()
    {
        var ledger = ReadLedger();
        ledger["unexpected"] = true;

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Equal(
            ["$: property 'unexpected' is not declared in coverage-ledger.schema.json."],
            findings);
    }

    [Fact]
    public void Json_schema_rejects_a_status_outside_the_declared_state_vocabulary()
    {
        var ledger = ReadLedger();
        Entry(ledger, EntryId)["status"] = "not-a-state";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Equal(
            ["$.entries[2].status: value 'not-a-state' is not allowed by coverage-ledger.schema.json."],
            findings);
    }

    [Fact]
    public void Replacing_one_baseline_row_with_a_duplicate_does_not_evade_the_denominator_guard()
    {
        var ledger = ReadLedger();
        var entries = ledger["entries"]!.AsArray();
        entries[entries.Count - 1] = Entry(ledger, EntryId).DeepClone();

        var findings = CreateEvidenceValidator().Validate(ledger);

        AssertExactFindings(
            findings,
            "$.entries: baseline entry 'distributed-command-transport' is missing.",
            "$.entries: baseline entry 'runtime-activity-execution-inspection' occurs 2 times; expected exactly once.",
            "composition evidence: coverage row 'distributed-command-transport' is outside the reviewed ledger denominator.");
    }

    [Theory]
    [InlineData("planned", "planned")]
    [InlineData("missing", "planned")]
    [InlineData("planned", "implemented")]
    [InlineData("implemented", "evidence-complete")]
    [InlineData("evidence-complete", "performance-complete")]
    [InlineData("performance-complete", "ready")]
    [InlineData("planned", "externally-blocked")]
    [InlineData("externally-blocked", "planned")]
    [InlineData("ready", "evidence-complete")]
    public void Documented_state_transitions_are_allowed(string previousStatus, string currentStatus)
    {
        var previous = ReadLedger();
        var current = ReadLedger();
        Entry(previous, EntryId)["status"] = previousStatus;
        Entry(current, EntryId)["status"] = currentStatus;

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Empty(findings);
    }

    [Fact]
    public void Performance_redesign_may_return_a_row_to_planned()
    {
        var previous = ReadLedger();
        var current = ReadLedger();
        Entry(previous, EntryId)["status"] = "performance-complete";
        Entry(current, EntryId)["status"] = "planned";
        Entry(current, EntryId)["performanceVerdict"] = new JsonObject
        {
            ["outcome"] = "redesign",
            ["acceptedShape"] = "shared-linked",
            ["evidence"] = "#646 redesign verdict"
        };

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("missing", "implemented")]
    [InlineData("planned", "evidence-complete")]
    [InlineData("implemented", "performance-complete")]
    [InlineData("evidence-complete", "ready")]
    [InlineData("externally-blocked", "ready")]
    public void State_transitions_may_not_skip_required_evidence_stages(string previousStatus, string currentStatus)
    {
        var previous = ReadLedger();
        var current = ReadLedger();
        Entry(previous, EntryId)["status"] = previousStatus;
        Entry(current, EntryId)["status"] = currentStatus;

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Equal(
            [$"{EntryId}: status transition '{previousStatus}' -> '{currentStatus}' is not allowed."],
            findings);
    }

    [Fact]
    public void Excluded_is_terminal()
    {
        var previous = ReadLedger();
        var current = ReadLedger();
        Entry(previous, EntryId)["outcome"] = "explicit-exclusion";
        Entry(previous, EntryId)["status"] = "excluded";
        Entry(current, EntryId)["outcome"] = "explicit-exclusion";
        Entry(current, EntryId)["status"] = "planned";

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Equal(
            [$"{EntryId}: status transition 'excluded' -> 'planned' is not allowed because excluded is terminal."],
            findings);
    }

    [Fact]
    public void Performance_complete_may_return_to_planned_only_for_a_redesign_verdict()
    {
        var previous = ReadLedger();
        var current = ReadLedger();
        Entry(previous, EntryId)["status"] = "performance-complete";
        Entry(current, EntryId)["status"] = "planned";

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Equal(
            [$"{EntryId}: status transition 'performance-complete' -> 'planned' requires a current #646 redesign verdict."],
            findings);
    }

    [Theory]
    [InlineData("queryShapes")]
    [InlineData("concurrencySemantics")]
    [InlineData("failureWindows")]
    [InlineData("restartScenarios")]
    public void Immutable_activation_obligations_cannot_be_silently_removed(string obligationProperty)
    {
        var immutable = ReadLedger();
        var candidate = immutable.DeepClone().AsObject();
        var candidateEntry = Entry(candidate, EntryId);
        var removed = candidateEntry[obligationProperty]!.AsArray()[0]!.GetValue<string>();
        candidateEntry[obligationProperty]!.AsArray().RemoveAt(0);

        var findings = CreateValidator().ValidateImmutableBaseline(immutable, candidate);

        Assert.Equal(
            [$"{EntryId}: immutable {obligationProperty} obligation '{removed}' was removed."],
            findings);
    }

    [Fact]
    public void Obligations_added_after_activation_are_append_only_across_pull_requests()
    {
        var previous = ReadLedger();
        var current = previous.DeepClone().AsObject();
        Entry(previous, EntryId)["queryShapes"]!.AsArray().Add("later-reviewed-query");

        var findings = CreateValidator().ValidateTransition(previous, current);

        Assert.Equal(
            [$"{EntryId}: reviewed queryShapes obligation 'later-reviewed-query' was removed."],
            findings);
    }

    [Fact]
    public void Immutable_activation_baseline_ref_cannot_be_relocated()
    {
        var immutable = ReadLedger();
        var candidate = immutable.DeepClone().AsObject();
        candidate["baselineRef"] = new string('a', 40);

        var findings = CreateValidator().ValidateImmutableBaseline(immutable, candidate);

        Assert.Equal(
            [$"$: immutable baselineRef '{immutable["baselineRef"]!.GetValue<string>()}' changed to '{new string('a', 40)}'."],
            findings);
    }

    [Fact]
    public void Evidence_complete_requires_structured_scenario_coverage_for_every_provider()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Empty(findings);
    }

    [Fact]
    public void Evidence_complete_rejects_a_missing_restart_scenario()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var mongodb = entry["providerEvidence"]!["mongodb"]!.AsArray();
        var missingScenario = entry["restartScenarios"]![1]!.GetValue<string>();
        mongodb.Remove(mongodb.OfType<JsonObject>().Single(record =>
            record["restartScenario"]?.GetValue<string>() == missingScenario));

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: mongodb evidence does not cover restart scenario '{missingScenario}'.",
            findings);
    }

    [Fact]
    public void Evidence_complete_rejects_misfiled_provider_evidence()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()[0]!.AsObject();
        record["provider"] = "mongodb";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record '{record["scenarioId"]!.GetValue<string>()}' declares provider 'mongodb'.",
            findings);
    }

    [Fact]
    public void Implemented_rows_reject_misfiled_provider_evidence_before_they_are_complete()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        var record = EvidenceRecord(EntryId, "sqlite", "ordinary-round-trip");
        record["coverageEntryId"] = "runtime-checkpoint-commit";
        WriteEvidenceArtifacts([record]);
        entry["providerEvidence"]!["sqlite"] = new JsonArray(record);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record 'ordinary-round-trip' declares coverage entry 'runtime-checkpoint-commit'.",
            findings);
    }

    [Fact]
    public void Implemented_rows_reject_nonpassing_current_generation_evidence()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        var record = EvidenceRecord(EntryId, "sqlite", "ordinary-round-trip");
        record["outcome"] = "classified-readiness-failure";
        WriteEvidenceArtifacts([record]);
        entry["providerEvidence"]!["sqlite"] = new JsonArray(record);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record 'ordinary-round-trip' is not passing.",
            findings);
    }

    [Fact]
    public void Evidence_complete_requires_provider_native_query_evidence()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var queryShape = entry["queryShapes"]![0]!.GetValue<string>();
        var record = entry["providerEvidence"]!["postgresql"]!.AsArray()
            .OfType<JsonObject>()
            .Single(candidate => candidate["queryShape"]?.GetValue<string>() == queryShape);
        record.Remove("nativeEvidence");

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: postgresql query '{queryShape}' lacks provider-native evidence.",
            findings);
    }

    [Fact]
    public void Evidence_complete_requires_the_executable_source_scenario()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()[0]!.AsObject();
        record.Remove("sourceScenarioId");

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains("$.entries[2].providerEvidence.sqlite[0]: required property 'sourceScenarioId' is missing.", findings);
    }

    [Fact]
    public void Evidence_complete_binds_query_provenance_to_the_native_plan_artifact()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()
            .OfType<JsonObject>()
            .First(candidate => candidate.ContainsKey("queryShape"));
        var queryShape = record["queryShape"]!.GetValue<string>();
        record["nativeQueryIdentity"] = "different-route";
        var evidencePath = ArtifactPath(record["evidence"]!.GetValue<string>());
        File.WriteAllText(evidencePath, GroundworkEvidenceArtifactContract.ArtifactPayload(record).ToJsonString());
        record["evidenceSha256"] = GroundworkEvidenceArtifactContract.FileSha256(evidencePath);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite query '{queryShape}' native artifact does not bind route 'different-route'.",
            findings);
    }

    [Fact]
    public void Evidence_complete_requires_equivalent_result_hashes_for_each_scenario()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["mongodb"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        record["resultHash"] = "different-result";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: scenario '{scenarioId}' has non-equivalent provider result hashes.",
            findings);
    }

    [Fact]
    public void Evidence_complete_rejects_memory_backed_provider_substrates()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        record["topology"] = "in-memory";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record '{scenarioId}' requires topology 'file-backed-distinct-connections'; found 'in-memory'.",
            findings);
    }

    [Fact]
    public void Evidence_complete_rejects_missing_or_unverifiable_evidence_artifacts()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["postgresql"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        var evidence = record["evidence"]!.GetValue<string>();
        File.Delete(ArtifactPath(evidence));

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: postgresql evidence record '{scenarioId}' artifact '{evidence}' is unavailable.",
            findings);
    }

    [Fact]
    public void Evidence_complete_rejects_evidence_artifact_digest_or_payload_drift()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["mongodb"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        var evidence = record["evidence"]!.GetValue<string>();
        File.AppendAllText(ArtifactPath(evidence), "tampered");

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: mongodb evidence record '{scenarioId}' artifact '{evidence}' digest does not match its contents.",
            findings);
    }

    [Fact]
    public void Retained_observations_must_recompute_the_published_result_hash()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        var record = EvidenceRecord(EntryId, "sqlite", "ordinary-round-trip");
        record["observations"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "winner-count",
                ["value"] = "1"
            });
        record["resultHash"] = ResultHash(record);
        record["observations"]![0]!["value"] = "2";
        WriteEvidenceArtifacts([record]);
        entry["providerEvidence"]!["sqlite"] = new JsonArray(record);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record 'ordinary-round-trip' result hash does not bind its retained observations.",
            findings);
    }

    [Fact]
    public void Versioned_artifact_provenance_must_exactly_match_its_ledger_record()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        var record = EvidenceRecord(EntryId, "sqlite", "ordinary-round-trip");
        record["evidence"] = GroundworkEvidenceArtifactContract.VersionedEvidencePath(
            ExpectedGroundworkVersion,
            "sqlite",
            EntryId,
            "ordinary-round-trip");
        record["observations"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "winner-count",
                ["value"] = "1"
            });
        record["provenance"] = new JsonObject
        {
            ["elsaCommit"] = new string('a', 40),
            ["elsaTree"] = new string('b', 40),
            ["runIdentity"] = "fixture-publication"
        };
        record["resultHash"] = ResultHash(record);
        WriteEvidenceArtifacts([record]);
        entry["providerEvidence"]!["sqlite"] = new JsonArray(record);

        var evidencePath = ArtifactPath(record["evidence"]!.GetValue<string>());
        var artifact = JsonNode.Parse(File.ReadAllText(evidencePath))!.AsObject();
        artifact["provenance"]!["elsaTree"] = new string('c', 40);
        File.WriteAllText(evidencePath, artifact.ToJsonString());
        record["evidenceSha256"] = GroundworkEvidenceArtifactContract.FileSha256(evidencePath);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record 'ordinary-round-trip' does not match its durable artifact payload.",
            findings);
    }

    [Fact]
    public void Evidence_complete_binds_provider_metadata_and_execution_to_the_active_catalog()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlserver"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        record["providerIdentity"] = "groundwork-sqlite";
        record["executionPath"] = "memory-fixture";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlserver evidence record '{scenarioId}' requires provider identity 'groundwork-sqlserver'; found 'groundwork-sqlite'.",
            findings);
        Assert.Contains(
            $"{EntryId}: sqlserver evidence record '{scenarioId}' does not identify its executed source scenario and physical target.",
            findings);
    }

    [Fact]
    public void Evidence_complete_requires_a_current_provider_generation()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        foreach (var record in entry["providerEvidence"]!["sqlserver"]!.AsArray().OfType<JsonObject>())
            record["providerVersion"] = "0.0.0-invented";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                $"uses provider version '0.0.0-invented', not ledger Groundwork version '{ExpectedGroundworkVersion}'",
                StringComparison.Ordinal));
        Assert.Contains(
            $"{EntryId}: status 'evidence-complete' requires sqlserver provider evidence.",
            findings);
    }

    [Fact]
    public void Evidence_complete_uses_only_current_records_while_retaining_prior_generations()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        foreach (var provider in new[] { "sqlite", "sqlserver", "postgresql", "mongodb" })
        {
            var records = entry["providerEvidence"]![provider]!.AsArray();
            var retained = records[0]!.DeepClone().AsObject();
            retained["providerVersion"] = Prior88GroundworkVersion;
            retained["evidence"] = GroundworkEvidenceArtifactContract.VersionedEvidencePath(
                Prior88GroundworkVersion,
                provider,
                EntryId,
                retained["scenarioId"]!.GetValue<string>());
            WriteEvidenceArtifacts([retained]);
            records.Insert(0, retained);
        }

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.DoesNotContain(
            findings,
            finding => finding.Contains(Prior88GroundworkVersion, StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Contains("occurs 2 times", StringComparison.Ordinal));
    }

    [Fact]
    public void Evidence_complete_rejects_scenarios_outside_the_active_obligation_catalog()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()[0]!.AsObject();
        record["scenarioId"] = "invented-scenario";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence scenario 'invented-scenario' is outside the active scenario catalog.",
            findings);
    }

    [Fact]
    public void Evidence_complete_binds_each_scenario_id_to_its_declared_obligation_value()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var records = entry["providerEvidence"]!["sqlite"]!.AsArray()
            .OfType<JsonObject>()
            .Where(record => record.ContainsKey("queryShape"))
            .Take(2)
            .ToArray();
        var firstQueryShape = records[0]["queryShape"]!.GetValue<string>();
        var secondQueryShape = records[1]["queryShape"]!.GetValue<string>();
        records[0]["queryShape"] = secondQueryShape;
        records[1]["queryShape"] = firstQueryShape;
        WriteEvidenceArtifacts(records);

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence scenario 'queryShape:{firstQueryShape}' declares queryShape '{secondQueryShape}' instead of '{firstQueryShape}'.",
            findings);
        Assert.Contains(
            $"{EntryId}: sqlite evidence scenario 'queryShape:{secondQueryShape}' declares queryShape '{firstQueryShape}' instead of '{secondQueryShape}'.",
            findings);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("redesign")]
    public void Performance_complete_rejects_nonpassing_performance_verdicts(string outcome)
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "performance-complete";
        AddCompleteEvidence(entry);
        entry["performanceVerdict"] = new JsonObject
        {
            ["outcome"] = outcome,
            ["acceptedShape"] = "shared-linked",
            ["evidence"] = "#646 verdict"
        };

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: status 'performance-complete' requires a passing or reviewed not-hot-path #646 verdict; found '{outcome}'.",
            findings);
    }

    [Fact]
    public void Evidence_complete_rejects_a_classified_readiness_failure()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlite"]!.AsArray()[0]!.AsObject();
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        record["outcome"] = "classified-readiness-failure";

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlite evidence record '{scenarioId}' is not passing.",
            findings);
    }

    [Fact]
    public void Restart_evidence_requires_two_independent_clients()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "evidence-complete";
        AddCompleteEvidence(entry);
        var record = entry["providerEvidence"]!["sqlserver"]!.AsArray()
            .OfType<JsonObject>()
            .First(candidate => candidate.ContainsKey("restartScenario"));
        var scenarioId = record["scenarioId"]!.GetValue<string>();
        record["clients"] = 1;

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: sqlserver evidence record '{scenarioId}' must use at least two independent clients.",
            findings);
    }

    [Fact]
    public void Strict_performance_rows_reject_not_hot_path_verdicts()
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, EntryId);
        entry["status"] = "performance-complete";
        entry["requiredPerformanceVerdict"] = "pass";
        AddCompleteEvidence(entry);
        entry["performanceVerdict"] = new JsonObject
        {
            ["outcome"] = "not-hot-path",
            ["acceptedShape"] = "not-applicable",
            ["evidence"] = "#646 reviewed verdict"
        };

        var findings = CreateEvidenceValidator().Validate(ledger);

        Assert.Contains(
            $"{EntryId}: status 'performance-complete' requires a passing or reviewed not-hot-path #646 verdict; found 'not-hot-path'.",
            findings);
    }

    private static void AssertCheckpointFenceGeneration(
        string providerVersion,
        string attachmentRelativePath,
        string attachmentSha256,
        string evidenceCommit,
        string evidenceTree,
        string runIdentity,
        JsonObject? ledgerOverride = null)
    {
        var ledger = ledgerOverride ?? ReadLedger();
        var attachmentPath = Path.Combine(
            RepoRoot,
            attachmentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var attachmentRecords = JsonNode.Parse(File.ReadAllText(attachmentPath))?.AsArray()
                                    ?.OfType<JsonObject>()
                                    .ToArray()
                                ?? throw new InvalidOperationException(
                                    $"Checkpoint/fence ledger attachment '{attachmentPath}' is empty.");
        var expectedRecordsByEntry = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["runtime-checkpoint-commit"] = 28,
            ["runtime-execution-liveness"] = 4,
            ["runtime-post-commit-outbox"] = 4
        };
        var attachmentRecordsByEntry = attachmentRecords
            .GroupBy(record => record["coverageEntryId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(records => records.Key, records => records.Count(), StringComparer.Ordinal);
        var attachmentByKey = attachmentRecords.GroupBy(EvidenceTuple, StringComparer.Ordinal).ToArray();
        var attachmentKeys = attachmentByKey
            .Select(records => records.Key)
            .ToHashSet(StringComparer.Ordinal);
        var attachmentEntryIds = expectedRecordsByEntry.Keys.ToHashSet(StringComparer.Ordinal);
        var ledgerRecords = Entries(ledger)
            .Where(entry => attachmentEntryIds.Contains(EntryIdOf(entry)))
            .SelectMany(entry => entry["providerEvidence"]!.AsObject()
                .SelectMany(provider => provider.Value!.AsArray().OfType<JsonObject>()))
            .Where(record => record["providerVersion"]?.GetValue<string>() == providerVersion)
            .Where(record => attachmentKeys.Contains(EvidenceTuple(record)))
            .ToArray();
        var ledgerByKey = ledgerRecords.GroupBy(EvidenceTuple, StringComparer.Ordinal).ToArray();

        Assert.Equal(36, attachmentRecords.Length);
        Assert.Equal(attachmentSha256, GroundworkEvidenceArtifactContract.FileSha256(attachmentPath));
        Assert.Equal(
            expectedRecordsByEntry.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            attachmentRecordsByEntry.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(attachmentRecords, record =>
        {
            Assert.Equal(providerVersion, record["providerVersion"]?.GetValue<string>());
            Assert.Equal("pass", record["outcome"]?.GetValue<string>());
            var relativeEvidencePath = record["evidence"]!.GetValue<string>();
            Assert.StartsWith(
                $"versions/{providerVersion}/evidence/",
                relativeEvidencePath,
                StringComparison.Ordinal);
            var evidencePath = Path.Combine(
                RepoRoot,
                "specs/094-harden-groundwork-stores",
                relativeEvidencePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(
                record["evidenceSha256"]!.GetValue<string>(),
                GroundworkEvidenceArtifactContract.FileSha256(evidencePath));
            var artifact = JsonNode.Parse(File.ReadAllText(evidencePath))!.AsObject();
            Assert.Equal(
                record["manifestFingerprint"]!.GetValue<string>(),
                artifact["manifestFingerprint"]!.GetValue<string>());
            Assert.NotEmpty(artifact["observations"]!.AsArray());
            Assert.Equal(evidenceCommit, artifact["provenance"]!["elsaCommit"]!.GetValue<string>());
            Assert.Equal(evidenceTree, artifact["provenance"]!["elsaTree"]!.GetValue<string>());
            Assert.Equal(runIdentity, artifact["provenance"]!["runIdentity"]!.GetValue<string>());
        });
        Assert.All(attachmentByKey, records => Assert.Single(records));
        Assert.All(ledgerByKey, records => Assert.Single(records));
        Assert.Equal(
            attachmentByKey.Select(records => records.Key).Order(StringComparer.Ordinal),
            ledgerByKey.Select(records => records.Key).Order(StringComparer.Ordinal));
        foreach (var attachment in attachmentByKey)
        {
            var ledgerRecord = ledgerByKey.Single(records => records.Key == attachment.Key).Single();
            Assert.True(
                JsonNode.DeepEquals(attachment.Single(), ledgerRecord),
                $"Checkpoint/fence evidence tuple '{attachment.Key}' differs from its attachment record.");
        }
    }

    private static GroundworkCoverageLedgerValidator CreateValidator() =>
        new(SchemaPath, ExpectedEntryIds);

    private static GroundworkCoverageLedgerValidator CreateEvidenceValidator() =>
        new(SchemaPath, ExpectedEntryIds, TestEvidenceRoot);

    private static JsonObject ReadLedger()
    {
        var ledger = JsonNode.Parse(File.ReadAllText(LedgerPath))?.AsObject()
                     ?? throw new InvalidOperationException($"Coverage ledger '{LedgerPath}' is empty.");
        StageCurrentEvidenceArtifacts(ledger);
        return ledger;
    }

    private static JsonObject ReadImmutableActivationLedger()
    {
        var result = GroundworkRepositoryGit.Run(
            RepoRoot,
            "show",
            $"{ImmutableActivationLedgerRef}:{LedgerRelativePath}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Immutable activation ledger '{ImmutableActivationLedgerRef}:{LedgerRelativePath}' is unavailable: {result.StandardError.Trim()}");
        }

        return JsonNode.Parse(result.StandardOutput)?.AsObject()
               ?? throw new InvalidOperationException("Immutable activation coverage ledger is empty.");
    }

    private static IEnumerable<JsonObject> Entries(JsonObject ledger) =>
        ledger["entries"]?.AsArray().Select(node => node?.AsObject()
            ?? throw new InvalidOperationException("Coverage ledger contains a null entry."))
        ?? throw new InvalidOperationException("Coverage ledger has no entries array.");

    private static JsonObject Entry(JsonObject ledger, string id) =>
        Entries(ledger).Single(entry => EntryIdOf(entry) == id);

    private static string EntryIdOf(JsonObject entry) =>
        entry["id"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Coverage ledger entry has no id.");

    private static string EvidenceTuple(JsonObject record) => string.Join(
        '|',
        record["coverageEntryId"]?.GetValue<string>() ?? "<missing-entry>",
        record["scenarioId"]?.GetValue<string>() ?? "<missing-scenario>",
        record["provider"]?.GetValue<string>() ?? "<missing-provider>");

    private static void RemoveEvidenceGeneration(JsonObject ledger, string providerVersion)
    {
        foreach (var providerRecords in Entries(ledger)
                     .SelectMany(entry => entry["providerEvidence"]!.AsObject())
                     .Select(provider => provider.Value!.AsArray()))
        {
            for (var index = providerRecords.Count - 1; index >= 0; index--)
            {
                if (providerRecords[index]?["providerVersion"]?.GetValue<string>() == providerVersion)
                    providerRecords.RemoveAt(index);
            }
        }
    }

    private static void AddCompleteEvidence(JsonObject entry)
    {
        var entryId = EntryIdOf(entry);
        var providerEvidence = entry["providerEvidence"]!.AsObject();
        foreach (var provider in new[] { "sqlite", "sqlserver", "postgresql", "mongodb" })
        {
            var records = new JsonArray
            {
                EvidenceRecord(entryId, provider, "ordinary-round-trip")
            };
            AddObligationRecords(records, entry, entryId, provider, "queryShapes", "queryShape");
            AddObligationRecords(records, entry, entryId, provider, "concurrencySemantics", "concurrencySemantic");
            AddObligationRecords(records, entry, entryId, provider, "failureWindows", "failureWindow");
            AddObligationRecords(records, entry, entryId, provider, "restartScenarios", "restartScenario");
            WriteEvidenceArtifacts(records);
            providerEvidence[provider] = records;
        }
    }

    private static void AddObligationRecords(
        JsonArray records,
        JsonObject entry,
        string entryId,
        string provider,
        string obligationCollection,
        string evidenceProperty)
    {
        foreach (var obligation in entry[obligationCollection]!.AsArray().Select(node => node!.GetValue<string>()))
        {
            var scenarioId = $"{evidenceProperty}:{obligation}";
            var record = EvidenceRecord(entryId, provider, scenarioId);
            record[evidenceProperty] = obligation;
            if (evidenceProperty == "queryShape")
            {
                record["nativeQueryIdentity"] = $"fixture-{obligation}";
                record["documentKind"] = "fixture-document";
                record["nativeEvidence"] = GroundworkEvidenceArtifactContract.NativeEvidencePath(provider, entryId, scenarioId);
            }
            records.Add(record);
        }
    }

    private static JsonObject EvidenceRecord(string entryId, string provider, string scenarioId)
    {
        return new JsonObject
        {
            ["scenarioId"] = scenarioId,
            ["sourceScenarioId"] = "fixture-source",
            ["coverageEntryId"] = entryId,
            ["provider"] = provider,
            ["providerIdentity"] = $"groundwork-{provider}",
            ["providerVersion"] = ExpectedGroundworkVersion,
            ["topology"] = provider switch
            {
                "sqlite" => "file-backed-distinct-connections",
                "sqlserver" => "real-sqlserver-container",
                "postgresql" => "real-postgresql-container",
                "mongodb" => "transaction-capable-replica-set",
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown test provider.")
            },
            ["manifestFingerprint"] = new string('a', 64),
            ["executionPath"] = $"provider-driver/{provider}/fixture-source/{new string('a', 16)}",
            ["clients"] = 2,
            ["resultHash"] = new string('b', 64),
            ["outcome"] = "pass",
            ["evidence"] = GroundworkEvidenceArtifactContract.EvidencePath(provider, entryId, scenarioId)
        };
    }

    private static string ResultHash(JsonObject record)
    {
        var observations = record["observations"]!.AsArray()
            .Select(candidate => candidate!.AsObject())
            .OrderBy(observation => observation["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(observation => observation["value"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(observation =>
            {
                var name = observation["name"]!.GetValue<string>();
                var value = observation["value"]!.GetValue<string>();
                return $"{name.Length}:{name}{value.Length}:{value}";
            });
        var digestInput = string.Join(
            '\n',
            new[]
            {
                record["sourceScenarioId"]!.GetValue<string>(),
                record["coverageEntryId"]!.GetValue<string>(),
                record["outcome"]!.GetValue<string>(),
                record["failureWindow"]?.GetValue<string>() ?? "-"
            }.Concat(observations));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput))).ToLowerInvariant();
    }

    private static void WriteEvidenceArtifacts(IEnumerable<JsonNode?> records)
    {
        foreach (var record in records.OfType<JsonObject>())
        {
            if (record["nativeEvidence"]?.GetValue<string>() is { } nativeEvidence)
            {
                var nativePath = ArtifactPath(nativeEvidence);
                Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
                File.WriteAllText(
                    nativePath,
                    $"provider={record["provider"]!.GetValue<string>()}\n" +
                    $"document-kind={record["documentKind"]!.GetValue<string>()}\n" +
                    $"route={record["nativeQueryIdentity"]!.GetValue<string>()}\n");
                record["nativeEvidenceSha256"] = GroundworkEvidenceArtifactContract.FileSha256(nativePath);
            }

            var evidencePath = ArtifactPath(record["evidence"]!.GetValue<string>());
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.WriteAllText(
                evidencePath,
                GroundworkEvidenceArtifactContract.ArtifactPayload(record).ToJsonString());
            record["evidenceSha256"] = GroundworkEvidenceArtifactContract.FileSha256(evidencePath);
        }
    }

    private static void StageCurrentEvidenceArtifacts(JsonObject ledger)
    {
        foreach (var record in Entries(ledger)
                     .SelectMany(entry => entry["providerEvidence"]!.AsObject().SelectMany(provider =>
                         provider.Value!.AsArray().OfType<JsonObject>()))
                     .Where(record => record["providerVersion"]?.GetValue<string>() == ExpectedGroundworkVersion))
        {
            StageArtifact(record["evidence"]!.GetValue<string>());
            if (record["nativeEvidence"]?.GetValue<string>() is { } nativeEvidence)
                StageArtifact(nativeEvidence);
        }
    }

    private static void StageArtifact(string relativePath)
    {
        var sourcePath = Path.Combine(
            RepoRoot,
            "specs",
            "094-harden-groundwork-stores",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetPath = ArtifactPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static string ArtifactPath(string relativePath) => Path.Combine(
        TestEvidenceRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertExactFindings(IReadOnlyCollection<string> actual, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

    private static bool IsGeneratedPath(string path)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return relativePath.StartsWith(".git/", StringComparison.Ordinal) ||
               relativePath.StartsWith("artifacts/", StringComparison.Ordinal) ||
               relativePath.Contains("/bin/", StringComparison.Ordinal) ||
               relativePath.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string LedgerPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "coverage-ledger.json");

    private static string SchemaPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "contracts",
        "coverage-ledger.schema.json");

    private static string TestEvidenceRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        $"elsa-groundwork-evidence-{Environment.ProcessId}");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }
}
