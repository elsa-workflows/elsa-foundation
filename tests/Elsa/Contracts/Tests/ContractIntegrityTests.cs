using System.Text.Json;
using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Committed-artifact integrity (spec 149 FR-004/FR-005 + the completeness rule FR-001):
/// manifest fingerprints verify against fragment bytes (the consumer's pinned-commit check), every
/// fragment maps to a src project, and — for every contract-relevant assembly in this test's closure —
/// the embedded <c>elsa.contract.json</c> resource is byte-identical to the committed fragment.
/// </summary>
public sealed class ContractIntegrityTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ContractsRoot = Path.Combine(RepoRoot, "docs", "contracts");

    private static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return current.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Manifest_fingerprints_match_fragment_bytes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot, "manifest.json")));
        var fragments = manifest.RootElement.GetProperty("fragments").EnumerateArray().ToArray();
        Assert.NotEmpty(fragments);

        foreach (var entry in fragments)
        {
            var assembly = entry.GetProperty("assembly").GetString()!;
            var expected = entry.GetProperty("fingerprint").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(ContractsRoot, "fragments", assembly + ".json"));
            Assert.Equal(expected, DeterministicJson.Fingerprint(bytes));
        }
    }

    [Fact]
    public void Every_committed_fragment_corresponds_to_a_src_project()
    {
        var projectNames = ContractsMerge.EnumerateContractProjects(Path.Combine(RepoRoot, "src"))
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fragment in Directory.EnumerateFiles(Path.Combine(ContractsRoot, "fragments"), "*.json"))
            Assert.Contains(Path.GetFileNameWithoutExtension(fragment), projectNames);
    }

    [Fact]
    public void Activity_entries_never_carry_server_state_fields()
    {
        // Overlay fields of the served catalog (spec FR-012) must be absent from activity contract
        // entries. Structure/submit payload SCHEMAS legitimately mention activityVersionId as a property
        // name consumers author, so the assertion is structural, not a raw text scan.
        foreach (var fragmentPath in Directory.EnumerateFiles(Path.Combine(ContractsRoot, "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(fragmentPath));
            if (!fragment.RootElement.TryGetProperty("activities", out var activities))
                continue;

            foreach (var activity in activities.EnumerateArray())
            {
                Assert.False(activity.TryGetProperty("activityVersionId", out _), fragmentPath);
                Assert.False(activity.TryGetProperty("available", out _), fragmentPath);
                Assert.False(activity.TryGetProperty("availabilityReason", out _), fragmentPath);
                Assert.False(activity.TryGetProperty("provenance", out _), fragmentPath);
            }
        }
    }

    [Fact]
    public void Embedded_resource_equals_the_committed_fragment_for_every_closure_assembly()
    {
        // FR-004: embedded bytes == committed bytes. Verified over every assembly in this test's output
        // that has a committed fragment (Http, Sequence, ControlFlow, Flowchart, Jint, Design.Api, ...).
        var verified = 0;
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "Elsa.*.dll"))
        {
            var committedPath = Path.Combine(ContractsRoot, "fragments", Path.GetFileNameWithoutExtension(dll) + ".json");
            if (!File.Exists(committedPath))
                continue;

            var assembly = System.Reflection.Assembly.LoadFrom(dll);
            using var resource = assembly.GetManifestResourceStream("elsa.contract.json");
            Assert.True(resource is not null,
                $"'{Path.GetFileName(dll)}' has a committed fragment but no embedded elsa.contract.json resource — rebuild after regenerating contracts.");

            using var memory = new MemoryStream();
            resource!.CopyTo(memory);
            Assert.True(memory.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(committedPath)),
                $"Embedded fragment of '{Path.GetFileName(dll)}' differs from the committed docs/contracts fragment — rebuild after regenerating contracts.");
            verified++;
        }

        Assert.True(verified >= 5, $"Only {verified} assemblies verified; the test closure should carry several contributing assemblies.");
    }
}
