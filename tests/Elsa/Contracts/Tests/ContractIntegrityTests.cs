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

    /// <summary>
    /// hosts.json is the third term of consumer availability (fragments ∩ shells.json ∩ hosts.json[host]).
    /// It only earns that role if it names real fragments, covers every shipped host, and is genuinely
    /// narrower than the fragment set — otherwise it would silently re-assert the two-way rule that let a
    /// consumer conclude a feature was available when its assembly was absent from the image.
    /// </summary>
    [Fact]
    public void Hosts_index_names_only_real_fragments_and_is_narrower_than_the_full_set()
    {
        using var hosts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot, "hosts.json")));
        var allFragments = Directory.EnumerateFiles(Path.Combine(ContractsRoot, "fragments"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        var entries = hosts.RootElement.GetProperty("hosts").EnumerateArray().ToArray();
        Assert.NotEmpty(entries);

        foreach (var host in entries)
        {
            var shipped = host.GetProperty("fragments").EnumerateArray().Select(f => f.GetString()!).ToArray();
            Assert.All(shipped, name => Assert.Contains(name, allFragments));
            Assert.Equal(shipped.OrderBy(name => name, StringComparer.Ordinal), shipped);
            Assert.True(shipped.Length < allFragments.Count,
                $"Host '{host.GetProperty("host").GetString()}' claims every fragment; the index would then be indistinguishable from the fragment set and useless as an availability term.");
        }
    }

    /// <summary>
    /// shells.json is keyed by feature id, so the index must publish feature ids too — otherwise a
    /// consumer still has to join through fragments to answer "can I enable this?". Every published id
    /// must come from a fragment the same host ships.
    /// </summary>
    [Fact]
    public void Hosts_index_publishes_feature_ids_backed_by_the_same_host_fragments()
    {
        using var hosts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot, "hosts.json")));

        foreach (var host in hosts.RootElement.GetProperty("hosts").EnumerateArray())
        {
            var features = host.GetProperty("features").EnumerateArray().Select(f => f.GetString()!).ToArray();
            Assert.NotEmpty(features);
            Assert.Equal(features.OrderBy(id => id, StringComparer.Ordinal), features);
            Assert.Equal(features.Distinct(StringComparer.Ordinal).Count(), features.Length);

            var idsFromShippedFragments = host.GetProperty("fragments").EnumerateArray()
                .SelectMany(fragment =>
                {
                    using var document = JsonDocument.Parse(
                        File.ReadAllBytes(Path.Combine(ContractsRoot, "fragments", fragment.GetString() + ".json")));
                    return document.RootElement.GetProperty("features").EnumerateArray()
                        .Select(feature => feature.GetProperty("id").GetString()!)
                        .ToArray();
                })
                .ToHashSet(StringComparer.Ordinal);

            Assert.All(features, id => Assert.Contains(id, idsFromShippedFragments));
        }
    }

    [Fact]
    public void Every_app_host_appears_in_the_hosts_index()
    {
        using var hosts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot, "hosts.json")));
        var indexed = hosts.RootElement.GetProperty("hosts").EnumerateArray()
            .Select(host => host.GetProperty("host").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var appProjects = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Apps"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.NotEmpty(appProjects);
        Assert.All(appProjects, app => Assert.Contains(app!, indexed));
    }

    [Fact]
    public void Manifest_fingerprints_the_hosts_index_and_submit_schema()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot, "manifest.json")));

        Assert.Equal(
            manifest.RootElement.GetProperty("hosts").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(Path.Combine(ContractsRoot, "hosts.json"))));
        Assert.Equal(
            manifest.RootElement.GetProperty("submitSchema").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(Path.Combine(ContractsRoot, "submit-schema.json"))));
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
