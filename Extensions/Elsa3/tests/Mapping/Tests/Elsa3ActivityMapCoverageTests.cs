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

    /// <summary>
    /// Spec 267 FR-008: every carried or narrowed input records what a JSON <c>null</c> converts to, or carries the Elsa 3
    /// default it emits for one (R2, or R5 with a default). A member with neither is a gap in the spec's table.
    /// </summary>
    [Fact]
    public void Every_carried_or_narrowed_input_records_its_null_conversion()
    {
        var gaps = Map.Entries
            .SelectMany(entry => entry.Members.Select(rule => (entry.Elsa3TypeName, Rule: rule)))
            .Where(item => item.Rule is { Kind: Elsa3MemberKind.Input, Rule: Elsa3MemberRuleKind.Carry or Elsa3MemberRuleKind.NarrowedContract })
            .Where(item => item.Rule.OnNull is null && item.Rule.Elsa3Default is null)
            .Select(item => $"{item.Elsa3TypeName}.{item.Rule.Elsa3Name}");

        Assert.Empty(gaps);
    }

    /// <summary>
    /// Spec 267 research R-7: the Elsa 4 side of the null table holds in the real catalog. A typed null needs an input that
    /// accepts null and is optional; no binding needs an optional input.
    /// </summary>
    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public async Task Every_null_conversion_fits_the_real_catalog(string elsa3Type)
    {
        var entry = Map.Find(elsa3Type)!;
        var typeKey = ((Elsa3ActivityTarget.CatalogActivity)entry.Target).ActivityTypeKey;
        var catalog = RealActivityCatalog.Create();
        var version = await catalog.GetVersion(catalog.VersionIdOf(typeKey));

        foreach (var rule in entry.Members.Where(rule => rule.OnNull is not null))
        {
            var input = version.Inputs.Single(input => input.ReferenceKey == rule.Elsa4Key);
            var name = $"'{elsa3Type}.{rule.Elsa3Name}'";
            // A refusal that only applies when another member is bound converts to a typed null otherwise.
            if (rule.OnNull is { Rule: Elsa3NullRule.TypedNull } or { Rule: Elsa3NullRule.Refuse, WhenBound: not null })
                Assert.True(input.IsNullable && !input.IsRequired, $"{name} converts a null to a typed null, but '{typeKey}.{input.ReferenceKey}' does not accept one.");
            if (rule.OnNull is { Rule: Elsa3NullRule.NoBinding })
                Assert.False(input.IsRequired, $"{name} leaves a null unbound, but '{typeKey}.{input.ReferenceKey}' is required.");
        }
    }

    /// <summary>
    /// #3297: the intrinsics' side of the null table holds in their contracts. Every intrinsic input is required and
    /// accepts no null (<see cref="Elsa3IntrinsicContracts"/>), so no intrinsic row may leave a null unbound or convert
    /// it to a typed null, and an emitted value must fit the input's type.
    /// </summary>
    [Fact]
    public void Every_intrinsic_null_conversion_fits_its_contract()
    {
        foreach (var entry in Map.Entries.Where(entry => entry.Target is Elsa3ActivityTarget.Intrinsic))
        {
            var contract = Elsa3IntrinsicContracts.Resolve(((Elsa3ActivityTarget.Intrinsic)entry.Target).ActivityVersionId, entry);
            foreach (var rule in entry.Members.Where(rule => rule.OnNull is not null))
            {
                var input = contract.Inputs.Single(input => input.ReferenceKey == rule.Elsa4Key);
                var name = $"'{entry.Elsa3TypeName}.{rule.Elsa3Name}'";
                if (rule.OnNull is { Rule: Elsa3NullRule.TypedNull } or { Rule: Elsa3NullRule.Refuse, WhenBound: not null })
                    Assert.True(input.IsNullable && !input.IsRequired, $"{name} converts a null to a typed null, but intrinsic input '{input.ReferenceKey}' does not accept one.");
                if (rule.OnNull is { Rule: Elsa3NullRule.NoBinding })
                    Assert.False(input.IsRequired, $"{name} leaves a null unbound, but intrinsic input '{input.ReferenceKey}' is required and has no default.");
                if (rule.OnNull is { Rule: Elsa3NullRule.Emit, Value: var value })
                    Assert.True(EmittedValueFits(value, IntrinsicClrType(input.Type)),
                        $"{name} emits '{value}' for a null, which does not fit intrinsic input '{input.ReferenceKey}' of type '{input.Type.Alias}'.");
            }
        }
    }

    /// <summary>
    /// #3297: an emitted value (<see cref="Elsa3NullRule.Emit"/>) is bound as a literal, so it must fit the Elsa 4
    /// input's type, or publication refuses the converted workflow.
    /// </summary>
    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public async Task Every_emitted_null_value_fits_the_elsa4_input_type(string elsa3Type)
    {
        var entry = Map.Find(elsa3Type)!;
        var typeKey = ((Elsa3ActivityTarget.CatalogActivity)entry.Target).ActivityTypeKey;
        var catalog = RealActivityCatalog.Create();
        var version = await catalog.GetVersion(catalog.VersionIdOf(typeKey));

        foreach (var rule in entry.Members.Where(rule => rule.OnNull is { Rule: Elsa3NullRule.Emit }))
        {
            var input = version.Inputs.Single(input => input.ReferenceKey == rule.Elsa4Key);
            var value = rule.OnNull!.Value;
            Assert.True(EmittedValueFits(value, InputProperty(typeKey, input).PropertyType),
                $"'{elsa3Type}.{rule.Elsa3Name}' emits '{value}' for a null, which does not fit '{typeKey}.{input.ReferenceKey}' of type '{input.Type.Alias}'.");
        }
    }

    /// <summary>
    /// #3297: a <see cref="Elsa3NullRule.NoBinding"/> row claims that Elsa 4's default for the input equals what Elsa 3
    /// ran with (spec 267's per-member table), so a converted null runs as before. The value an unbound input runs with
    /// is the one publication pins for it (<see cref="UnboundValue"/>). If an Elsa 4 default changes, this fails instead
    /// of converted workflows changing behaviour silently. The Elsa 3 values are spec 267's "what Elsa 3 ran with"
    /// column, as JSON.
    /// </summary>
    [Fact]
    public async Task Every_unbound_null_runs_with_the_value_elsa3_ran_with()
    {
        var elsa3RanWith = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elsa.If.Condition"] = "false",
            ["Elsa.While.Condition"] = "false",
            ["Elsa.FlowDecision.Condition"] = "false",
            ["Elsa.For.Start"] = "0",
            ["Elsa.For.End"] = "0",
            ["Elsa.HttpEndpoint.Authorize"] = "false",
            ["Elsa.SendHttpRequest.Method"] = "\"GET\"",
            ["Elsa.FlowSendHttpRequest.Method"] = "\"GET\"",
            ["Elsa.WriteHttpResponse.StatusCode"] = "200",
            ["Elsa.PublishEvent.IsLocalEvent"] = "false",
            ["Elsa.DispatchWorkflow.WaitForCompletion"] = "false",
        };
        var catalog = RealActivityCatalog.Create();
        var unbound = Map.Entries
            .SelectMany(entry => entry.Members.Select(rule => (Entry: entry, Rule: rule)))
            .Where(item => item.Rule.OnNull is { Rule: Elsa3NullRule.NoBinding })
            .ToArray();

        // Every no-binding row states what Elsa 3 ran with, and every stated value belongs to a no-binding row.
        Assert.Equal(
            elsa3RanWith.Keys.Order(StringComparer.Ordinal),
            unbound.Select(item => $"{item.Entry.Elsa3TypeName}.{item.Rule.Elsa3Name}").Order(StringComparer.Ordinal));

        var mismatches = new List<string>();
        foreach (var (entry, rule) in unbound)
        {
            var name = $"{entry.Elsa3TypeName}.{rule.Elsa3Name}";
            // Intrinsics are covered by Every_intrinsic_null_conversion_fits_its_contract: their inputs are required, so
            // a no-binding row on one already fails there.
            if (entry.Target is not Elsa3ActivityTarget.CatalogActivity { ActivityTypeKey: var typeKey })
                continue;

            var expected = JsonDocument.Parse(elsa3RanWith[name]).RootElement;
            var input = (await catalog.GetVersion(catalog.VersionIdOf(typeKey))).Inputs.Single(input => input.ReferenceKey == rule.Elsa4Key);
            var problem = UnboundValue(input, InputProperty(typeKey, input).PropertyType) switch
            {
                (null, var refusal) => refusal,
                ({ } runsWith, _) when !JsonElement.DeepEquals(runsWith, expected) => $"runs unbound with {runsWith}",
                _ => null
            };
            if (problem is not null)
                mismatches.Add($"'{name}': Elsa 3 ran with {expected}, but '{typeKey}.{input.ReferenceKey}' {problem}.");
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// Spec 248 D-3: an entry without a <c>canStartWorkflow</c> rule drops the flag, because Elsa 3 only uses it there to
    /// pick a flowchart's start. That is faithful only for non-triggers, so exactly the Elsa 3 triggers declare one.
    /// </summary>
    [Fact]
    public void Exactly_the_elsa3_triggers_with_a_counterpart_classify_canStartWorkflow()
    {
        string[] triggers = ["Elsa.Cron", "Elsa.Event", "Elsa.HttpEndpoint", "Elsa.StartAt", "Elsa.Timer"];
        var classifying = Map.Entries
            .Where(entry => entry.Members.Any(rule =>
                rule.Kind == Elsa3MemberKind.CustomProperty && StringComparer.OrdinalIgnoreCase.Equals(rule.Elsa3Name, "CanStartWorkflow")))
            .Select(entry => entry.Elsa3TypeName)
            .Order(StringComparer.Ordinal);
        var expected = triggers.Where(type => Map.Find(type) is { Target: not Elsa3ActivityTarget.NoCounterpart });

        Assert.Equal(expected, classifying);
    }

    /// <summary>
    /// The member map's "Elsa 4 required members with no Elsa 3 source": these have no R2 default, so an unset member is
    /// refused. WriteLine.text left the list in spec 248: Elsa 3 writes an empty line, so it defaults to "".
    /// </summary>
    [Fact]
    public void Required_members_with_no_elsa3_source_have_no_default_and_WriteLine_text_defaults_to_empty()
    {
        (string Type, string Elsa4Key)[] unsourced =
        [
            ("Elsa.SendHttpRequest", "Url"), ("Elsa.HttpEndpoint", "Path"), ("Elsa.Event", "EventName"),
            ("Elsa.PublishEvent", "EventName"), ("Elsa.Cron", "Expression"), ("Elsa.DispatchWorkflow", "WorkflowDefinitionId"),
            ("Elsa.RunJavaScript", "script")
        ];

        Assert.All(unsourced, member => Assert.DoesNotContain(Map.Find(member.Type)!.Members, rule =>
            rule.Rule == Elsa3MemberRuleKind.DefaultFlip && rule.Elsa4Key == member.Elsa4Key));
        var text = Assert.Single(Map.Find("Elsa.WriteLine")!.Members);
        Assert.Equal(Elsa3MemberRuleKind.DefaultFlip, text.Rule);
        Assert.Equal("text", text.Elsa4Key);
        Assert.Equal(string.Empty, text.Elsa3Default?.GetString());
    }

    /// <summary>
    /// Spec 256: WriteHttpResponse.Content carries any value Elsa 4 sends the same (the body is a JSON value), and its
    /// content type drops a blank expression. These contracts read the WriteHttpResponse siblings, so no other member uses them.
    /// </summary>
    [Fact]
    public void WriteHttpResponse_content_and_content_type_use_the_response_contracts_only()
    {
        var members = Map.Find("Elsa.WriteHttpResponse")!.Members;
        var content = Assert.Single(members, rule => rule.Elsa3Name == "Content");
        Assert.Equal("Body", content.Elsa4Key);
        Assert.Equal(Elsa3ValueContract.HttpResponseBody, content.Contract);
        var contentType = Assert.Single(members, rule => rule.Elsa3Name == "ContentType");
        Assert.Equal("ContentType", contentType.Elsa4Key);
        Assert.Equal(Elsa3ValueContract.HttpResponseContentType, contentType.Contract);

        var users = Map.Entries
            .Where(entry => entry.Members.Any(rule =>
                rule.Contract is Elsa3ValueContract.HttpResponseBody or Elsa3ValueContract.HttpResponseContentType))
            .Select(entry => entry.Elsa3TypeName);
        Assert.Equal(["Elsa.WriteHttpResponse"], users);
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
        // raises the third for connections Elsa 3 never follows (spec 239 G-7); the member pass raises the fourth for a null
        // member bound by name (#3293).
        string[] raisedByRewrites =
        [
            Elsa3ActivityDiagnosticCodes.OutcomeUndeclared,
            Elsa3ActivityDiagnosticCodes.OutcomeUnreached,
            Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved,
            Elsa3ActivityDiagnosticCodes.NullTakesElsa4Default
        ];

        Assert.All(declared, code => Assert.Contains(code, codes));
        Assert.All(codes, code => Assert.True(declared.Contains(code) || raisedByRewrites.Contains(code), $"Behaviour code '{code}' is used by no map entry."));
    }

    /// <summary>
    /// Whether <paramref name="value"/>, bound as the normalizer binds an emitted null value (a JSON array or object stays
    /// JSON, anything else is text), converts to <paramref name="clrType"/> the way publication converts a literal
    /// (<c>RuntimeInputBindingCompiler.ConvertLiteral</c>): JSON deserializes with default options, text parses as an
    /// enum name or converts invariantly. Publication's comma-list coercion of text into a collection is not modelled, so
    /// an emitted collection must be written as a JSON array.
    /// </summary>
    private static bool EmittedValueFits(string? value, Type clrType)
    {
        if (value is null)
            return false;

        var target = Nullable.GetUnderlyingType(clrType) ?? clrType;
        try
        {
            if (value.TrimStart() is ['[' or '{', ..])
                JsonDocument.Parse(value).RootElement.Deserialize(target);
            else if (target.IsEnum)
                return Enum.TryParse(target, value, ignoreCase: true, out _);
            else if (target != typeof(string) && !target.IsInstanceOfType(value))
            {
                if (typeof(IConvertible).IsAssignableFrom(target))
                    Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
                else
                    JsonSerializer.SerializeToElement(value).Deserialize(target);
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidCastException or OverflowException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The value an unbound input runs with, as publication pins it (<c>RuntimeInputBindingCompiler.CompileUnbound</c>):
    /// its declared default; else, for an optional input, null when it accepts null and its CLR type's default when that
    /// is a value type. The activity's property initializer is never used, because hydration sets every input. Returns
    /// the refusal instead when publication refuses the input unbound.
    /// </summary>
    private static (JsonElement? RunsWith, string? Refusal) UnboundValue(InputDefinition input, Type propertyType)
    {
        if (input.DefaultValue is { } declared)
            return (declared, null);
        if (input.IsRequired)
            return (null, "is required with no declared default, so publication refuses it unbound");
        if (input.IsNullable)
            return (JsonSerializer.SerializeToElement<object?>(null), null);

        var clrType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return clrType.IsValueType
            ? (JsonSerializer.SerializeToElement(Activator.CreateInstance(clrType), clrType), null)
            : (null, "has no declared default and a non-nullable reference type, so publication refuses it unbound");
    }

    /// <summary>The CLR types of the aliases the intrinsic contracts declare.</summary>
    private static Type IntrinsicClrType(Elsa.Primitives.Models.TypeReference type) => type.Alias switch
    {
        "String" => typeof(string),
        "Object" => typeof(object),
        _ => throw new InvalidOperationException($"Intrinsic input type '{type.Alias}' has no CLR type in this test; add it.")
    };

    /// <summary>The CLR property behind a catalog input: the activity type key is the CLR type name, the input name the property.</summary>
    private static PropertyInfo InputProperty(string activityTypeKey, InputDefinition input)
    {
        var type = RealActivityCatalog.Anchors
            .Select(anchor => anchor.Assembly).Distinct()
            .Select(assembly => assembly.GetType(activityTypeKey))
            .Single(type => type is not null)!;
        return type.GetProperty(input.Name) ?? throw new InvalidOperationException($"'{activityTypeKey}' has no property '{input.Name}'.");
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
