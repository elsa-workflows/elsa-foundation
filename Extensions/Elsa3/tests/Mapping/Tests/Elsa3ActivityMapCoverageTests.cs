using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 SC-001 and FR-002: the built-in map classifies every Elsa 3 activity and member of the test-owned surface,
/// and every Elsa 4 type key, member key, slot and outcome it targets exists, ordinally, in a catalog built by CLR
/// reconciliation of the real Elsa 4 activity assemblies.
/// </summary>
public sealed class Elsa3ActivityMapCoverageTests
{
    private static readonly IElsa3ActivityMap Map = new Elsa3BuiltInActivityMap();

    public static TheoryData<string> Elsa3Activities()
    {
        var data = new TheoryData<string>();
        foreach (var activity in Surface.Value)
            data.Add(activity.Type);
        return data;
    }

    public static TheoryData<string> CatalogEntries()
    {
        var data = new TheoryData<string>();
        foreach (var entry in Map.Entries.Where(entry => entry.Target is Elsa3ActivityTarget.CatalogActivity))
            data.Add(entry.Elsa3TypeName);
        return data;
    }

    [Theory]
    [MemberData(nameof(Elsa3Activities))]
    public void Every_elsa3_activity_and_member_is_classified_exactly_once(string elsa3Type)
    {
        var activity = Surface.Value.Single(item => item.Type == elsa3Type);
        var entry = Map.Find(elsa3Type);

        Assert.True(entry is not null, $"Elsa 3 activity '{elsa3Type}' has no map entry.");
        if (entry!.Target is Elsa3ActivityTarget.NoCounterpart)
            return;

        foreach (var member in activity.Members)
        {
            var rules = entry.Members.Where(rule => StringComparer.OrdinalIgnoreCase.Equals(rule.Elsa3Name, member.Name)).ToArray();
            Assert.True(rules.Length == 1, $"'{elsa3Type}.{member.Name}' is classified {rules.Length} times; expected exactly once.");
        }

        foreach (var rule in entry.Members.Where(rule => rule.Kind != Elsa3MemberKind.Synthetic))
            Assert.True(
                activity.Members.Any(member => StringComparer.OrdinalIgnoreCase.Equals(member.Name, rule.Elsa3Name)),
                $"'{elsa3Type}' classifies '{rule.Elsa3Name}', which the Elsa 3 surface does not declare.");
    }

    [Fact]
    public void Every_map_entry_is_an_elsa3_activity_of_the_surface()
    {
        var types = Surface.Value.Select(activity => activity.Type).ToHashSet(StringComparer.Ordinal);

        Assert.All(Map.Entries, entry => Assert.Contains(entry.Elsa3TypeName, types));
        Assert.Equal(Map.Entries.Count, Map.Entries.Select(entry => entry.Elsa3TypeName).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public async Task Every_targeted_elsa4_key_slot_and_outcome_exists_in_the_real_catalog(string elsa3Type)
    {
        var entry = Map.Find(elsa3Type)!;
        var typeKey = ((Elsa3ActivityTarget.CatalogActivity)entry.Target).ActivityTypeKey;
        var catalog = RealActivityCatalog.Create();
        var version = await catalog.GetVersion(catalog.VersionIdOf(typeKey));

        foreach (var rule in entry.Members)
        {
            switch (rule.Rule)
            {
                case Elsa3MemberRuleKind.Carry or Elsa3MemberRuleKind.DefaultFlip or Elsa3MemberRuleKind.NarrowedContract
                    when rule.Kind != Elsa3MemberKind.Output:
                    Assert.True(version.Inputs.Any(input => input.ReferenceKey == rule.Elsa4Key),
                        $"'{elsa3Type}.{rule.Elsa3Name}' targets input '{rule.Elsa4Key}', which '{typeKey}' does not declare.");
                    break;
                case Elsa3MemberRuleKind.Carry:
                    Assert.True(version.Outputs.Any(output => output.ReferenceKey == rule.Elsa4Key),
                        $"'{elsa3Type}.{rule.Elsa3Name}' targets output '{rule.Elsa4Key}', which '{typeKey}' does not declare.");
                    break;
                case Elsa3MemberRuleKind.LostOutput when rule.Projection is not null && !rule.Projection.Contains('.'):
                    Assert.True(version.Outputs.Any(output => output.ReferenceKey == rule.Projection),
                        $"'{elsa3Type}.{rule.Elsa3Name}' projects to output '{rule.Projection}', which '{typeKey}' does not declare.");
                    break;
                case Elsa3MemberRuleKind.ChildSlot:
                    Assert.True(Slots(version).Contains(rule.Elsa4Key!),
                        $"'{elsa3Type}.{rule.Elsa3Name}' targets slot '{rule.Elsa4Key}', which '{typeKey}' does not declare.");
                    break;
            }
        }

        var outcomes = Outcomes(version);
        foreach (var outcome in entry.Outcomes.SelectMany(rule => rule.Elsa4Outcomes).Where(name => !name.StartsWith('{')))
            Assert.True(outcomes.Contains(outcome) || outcome == "Done" && outcomes.Count == 0 || HasValueOutcomes(version),
                $"'{elsa3Type}' maps an outcome to '{outcome}', which '{typeKey}' does not declare.");
    }

    [Fact]
    public void Every_intrinsic_target_and_key_exists()
    {
        foreach (var entry in Map.Entries.Where(entry => entry.Target is Elsa3ActivityTarget.Intrinsic))
        {
            var target = (Elsa3ActivityTarget.Intrinsic)entry.Target;
            Assert.True(Elsa3IntrinsicContracts.IsKnown(target.ActivityVersionId), $"'{entry.Elsa3TypeName}' targets unknown intrinsic '{target.ActivityVersionId}'.");
            var contract = Elsa3IntrinsicContracts.Resolve(target.ActivityVersionId, entry);
            foreach (var rule in entry.Members.Where(rule => rule.Elsa4Key is not null))
                Assert.Contains(contract.Inputs, input => input.ReferenceKey == rule.Elsa4Key);
        }
    }

    [Fact]
    public void Every_behaviour_warning_is_a_declared_code_and_every_code_is_used()
    {
        var codes = typeof(Elsa3ActivityDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .Where(code => code.StartsWith("ELS3-BEHAVIOUR-", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var declared = Map.Entries
            .SelectMany(entry => entry.BehaviourWarnings.Concat(entry.Members.Select(rule => rule.BehaviourWarning)))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        // The terminal-placement rewrite raises the first two for reusable workflows (plan D-6, D-7); graph conversion
        // raises the third for connections Elsa 3 never follows (spec 239 G-7).
        string[] raisedByRewrites =
        [
            Elsa3ActivityDiagnosticCodes.OutcomeUndeclared,
            Elsa3ActivityDiagnosticCodes.OutcomeUnreached,
            Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved
        ];

        Assert.All(declared, code => Assert.Contains(code, codes));
        Assert.All(codes, code => Assert.True(declared.Contains(code) || raisedByRewrites.Contains(code), $"Behaviour code '{code}' is used by no map entry."));
    }

    private static IReadOnlySet<string> Slots(IActivityDefinitionVersion version) =>
        ActivityStructureDesignFacetReader.TryReadSingle(version.DesignFacets, out var facet) && facet is not null
            ? facet.Payload.GetProperty("slots").EnumerateArray()
                .Select(slot => slot.GetProperty("name").GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> Outcomes(IActivityDefinitionVersion version) =>
        version.DesignFacets.FirstOrDefault(facet => facet.Kind == "elsa.outcomes") is { } facet &&
        facet.Payload.TryGetProperty("ports", out var ports)
            ? ports.EnumerateArray().Select(port => port.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static bool HasValueOutcomes(IActivityDefinitionVersion version) =>
        version.DesignFacets.Any(facet => facet.Kind == "elsa.outcomes" && facet.Payload.TryGetProperty("dynamicOutcomes", out _));

    private static readonly Lazy<IReadOnlyList<SurfaceActivity>> Surface = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "elsa3-activity-surface.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("activities").EnumerateArray()
            .Select(activity => new SurfaceActivity(
                activity.GetProperty("type").GetString()!,
                activity.GetProperty("members").EnumerateArray()
                    .Select(member => new SurfaceMember(member.GetProperty("name").GetString()!, member.GetProperty("kind").GetString()!))
                    .ToArray()))
            .ToArray();
    });

    private sealed record SurfaceActivity(string Type, IReadOnlyList<SurfaceMember> Members);

    private sealed record SurfaceMember(string Name, string Kind);
}
