using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonBaselineArchitectureTests
{
    [Theory]
    [InlineData("class C { object x = new JsonSerializerOptions(); }", "construction")]
    [InlineData("class C { JsonSerializerOptions x = new(); }", "construction")]
    [InlineData("class C { JsonSerializerOptions Create() => new(); }", "construction")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { object x = new Opt(); }", "construction")]
    [InlineData("class C { object x = JsonSerializerOptions.Web; }", "defaults")]
    [InlineData("class C { object x = JsonSerializerOptions.Default; }", "defaults")]
    [InlineData("[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)] class C {}", "PropertyNameCaseInsensitive")]
    [InlineData("[JsonSourceGenerationOptions(Converters = [typeof(LossyConverter)])] class C {}", "converter:LossyConverter")]
    [InlineData("[JsonSourceGenerationOptions(UseStringEnumConverter = true)] class C {}", "converter:JsonStringEnumConverter")]
    [InlineData("class C { object x = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }; }", "UnmappedMemberHandling")]
    [InlineData("class C { void M() { options.MaxDepth = 128; } }", "MaxDepth")]
    [InlineData("class C { void M() { options.AllowTrailingCommas = true; } }", "AllowTrailingCommas")]
    [InlineData("class C { void M() { options.ReadCommentHandling = JsonCommentHandling.Skip; } }", "ReadCommentHandling")]
    [InlineData("class C { void M() { options.ReferenceHandler = ReferenceHandler.IgnoreCycles; } }", "ReferenceHandler")]
    [InlineData("class C { void M() { options.IgnoreReadOnlyProperties = true; } }", "IgnoreReadOnlyProperties")]
    [InlineData("class C { void M() { options.IncludeFields = true; } }", "IncludeFields")]
    [InlineData("class C { void M() { options.WriteIndented = true; } }", "WriteIndented")]
    [InlineData("class C { void M() { options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { options.Converters.Add(new LossyConverter()); } }", "converter:LossyConverter")]
    [InlineData("class C { void M() { options.Converters.Insert(0, new LossyConverter()); } }", "converter:LossyConverter")]
    [InlineData("class C { void M() { options.Converters.Clear(); } }", "converter-mutation:Clear")]
    [InlineData("class C { void M() { options.Converters.RemoveAt(0); } }", "converter-mutation:RemoveAt")]
    [InlineData("class C { void M() { options.Converters.Remove(converter); } }", "converter-mutation:Remove")]
    [InlineData("class C { void M() { options.TypeInfoResolver.Modifiers.Add(ChangeData); } }", "modifier:ChangeData")]
    [InlineData("class C { void M() { resolver.WithAddedModifier(ChangeData); } }", "modifier:ChangeData")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, options: null); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeToElement(value, value.GetType()); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeAsync(stream, value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.DeserializeAsync<T>(stream); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>(); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>(options: null); } }", "optionless")]
    [InlineData("using JS = System.Text.Json.JsonSerializer; class C { void M() { JS.Serialize(value); } }", "optionless")]
    [InlineData("using static System.Text.Json.JsonSerializer; class C { void M() { Serialize(value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeToNode(value); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize(typeof(UserType)); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeAsync(stream, value, cancellationToken: token); } }", "optionless")]
    public void Every_unregistered_deviation_is_rejected(string source, string setting)
    {
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(source).Where(x => x.Setting == setting));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), []));
    }

    [Fact]
    public void A_reasoned_register_entry_accounts_only_for_its_file_and_specific_setting()
    {
        var finding = Assert.Single(JsonBaselineSourceScan.Scan("class C { object x = new JsonSerializerOptions(); }"));
        var register = JsonBaselineSourceScan.ReadRegister("## Baseline deviations\n| File | Setting | Reason | Approved by |\n|---|---|---|---|\n| `owner.cs` | `construction` | Own format projection. | ADR 0091 |\n");
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), register));
        Assert.False(JsonBaselineSourceScan.IsAccounted("foreign.cs", finding, new HashSet<string>(), register));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding with { Setting = "Encoder" }, new HashSet<string>(), register));
    }

    [Fact]
    public void Legacy_accounting_never_admits_workflow_data_and_a_removed_migrated_entry_passes()
    {
        var call = Assert.Single(JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(value); } }"));
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity("owner.cs", call), JsonBaselineSourceScan.Identity("StatefulActivity.cs", call) };
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("foreign.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("StatefulActivity.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", call, new HashSet<string>(), []));
        var migrated = JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(value, JsonBaseline.Options); } }");
        Assert.Empty(migrated.Where(x => !JsonBaselineSourceScan.IsAccounted("owner.cs", x, new HashSet<string>(), [])));
    }

    [Fact]
    public void Baseline_copies_explicit_type_info_protected_defaults_comments_and_strings_are_accepted()
    {
        const string source = "class C { JsonSerializerOptions options = new(JsonBaseline.Options); void M() { options.PropertyNameCaseInsensitive = false; options.NumberHandling = JsonNumberHandling.Strict; JsonSerializer.Serialize(value, options); JsonSerializer.Serialize(value, Context.WireContext.Value); /* new JsonSerializerOptions() */ var text = \"JsonSerializer.Serialize(value)\"; } }";
        Assert.Empty(JsonBaselineSourceScan.Scan(source));
    }

    [Fact]
    public void A_domain_depth_property_and_owner_codec_are_not_serializer_settings_or_optionless_calls()
    {
        Assert.Empty(JsonBaselineSourceScan.Scan("class C { void M() { model.MaxDepth = 128; var parsing = new JsonDocumentOptions { MaxDepth = 128 }; parsing.MaxDepth = 256; RuntimeCompleteActivityPayloadMemo.Deserialize(workItem); var value = codec.Deserialize(input); } }"));
    }

    [Fact]
    public void Every_production_root_uses_the_baseline_or_a_specific_recorded_deviation()
    {
        var register = JsonBaselineSourceScan.ReadRegister(File.ReadAllText(Path.Join(RepoPaths.RepoRoot, "docs/serialization.md")));
        var legacyPath = Path.Join(RepoPaths.RepoRoot, "Essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt");
        Assert.True(File.Exists(legacyPath), "Missing shrinking option-less own-contract inventory.");
        var legacy = File.ReadAllLines(legacyPath).Where(line => line.Length > 0 && !line.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var paths = ModuleRoots.Resolve(RepoPaths.RepoRoot, [.. ModuleRoots.Production, "Studio/src", "Extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(path) && ModuleRoots.IsNotTestFile(path)).Distinct().ToArray();
        Assert.NotEmpty(paths);
        Assert.All(register, row => Assert.True(File.Exists(Path.Join(RepoPaths.RepoRoot, row.File)), $"Stale register path: {row.File}"));
        var failures = new List<string>();
        var observedLegacy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(RepoPaths.RepoRoot, path).Replace('\\', '/');
            foreach (var finding in JsonBaselineSourceScan.Scan(File.ReadAllText(path)))
            {
                if (finding.Setting == "optionless") observedLegacy.Add(JsonBaselineSourceScan.Identity(relative, finding));
                if (!JsonBaselineSourceScan.IsAccounted(relative, finding, legacy, register)) failures.Add($"{relative}:{finding.Line} | {finding.Setting} | {finding.Signature}");
            }
        }
        failures.AddRange(legacy.Except(observedLegacy).Select(identity => "Remove stale migrated legacy receipt: " + identity));
        Assert.True(failures.Count == 0, "Unregistered JSON options or new/workflow-data option-less calls:\n" + string.Join('\n', failures));
    }

    [Fact]
    public void Duplicate_calls_and_same_named_types_cannot_share_a_legacy_receipt()
    {
        var source = "namespace A { class C { void M() { JsonSerializer.Serialize(value); JsonSerializer.Serialize(value); } } } namespace B { class C { void M() { JsonSerializer.Serialize(value); } } }";
        var calls = JsonBaselineSourceScan.Scan(source);
        Assert.Equal(3, calls.Count);
        Assert.Equal(3, calls.Select(call => JsonBaselineSourceScan.Identity("owner.cs", call)).Distinct().Count());
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity("owner.cs", calls[0]) };
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", calls[0], legacy, []));
        Assert.All(calls.Skip(1), call => Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", call, legacy, [])));
    }
}
