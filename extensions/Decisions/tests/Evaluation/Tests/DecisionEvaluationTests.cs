using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CShells.Lifecycle;
using Elsa.Decisions.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Evaluation.Tests;

public class DecisionEvaluationTests
{
    private readonly DecisionEngine engine = new();

    [Theory]
    [InlineData("<= 1000", "1000", true)]
    [InlineData("<= 1000", "1000.01", false)]
    [InlineData("]1000..10000]", "1000", false)]
    [InlineData("]1000..10000]", "1000.01", true)]
    [InlineData("]1000..10000]", "10000", true)]
    [InlineData("]1000..10000]", "10000.01", false)]
    [InlineData("[1..2[", "2", false)]
    [InlineData("(1..2)", "1.5", true)]
    [InlineData("not(1, 2)", "3", true)]
    [InlineData("not(1, 2)", "2", false)]
    [InlineData("-0.0", "0", true)]
    [InlineData(".5", "0.500", true)]
    [InlineData("9007199254740993", "9007199254740993", true)]
    [InlineData("9007199254740993", "9007199254740992", false)]
    [InlineData("1234567890123456789012345678901234", "1234567890123456789012345678901234", true)]
    [InlineData("1", "1.00", true)]
    [InlineData("1000000000000000000000000000000000", "1000000000000000000000000000000000", true)]
    [InlineData("1000000000000000000000000000000000.0", "1000000000000000000000000000000000", true)]
    public void Exact_numeric_unary_tests(string expression, string input, bool matches)
    {
        var result = Evaluate(Table([expression]), new("number", input));
        Assert.Equal(matches ? "Success" : "NoMatch", result.Status);
        Assert.Equal(matches, Assert.Single(result.Trace).Matched);
    }

    [Theory]
    [InlineData("-", "null", null, false, "false")]
    [InlineData("null", "null", null, true, "true")]
    [InlineData("not(null)", "null", null, false, "false")]
    [InlineData("< 2", "null", null, false, "unknown")]
    [InlineData("not(< 2)", "null", null, false, "unknown")]
    [InlineData("< 2, null", "null", null, true, "true")]
    [InlineData("< null", "number", "1", false, "unknown")]
    [InlineData("null", "number", "1", false, "false")]
    public void Null_and_three_valued_matching(string expression, string kind, string? value, bool matches, string truth)
    {
        var result = Evaluate(Table([expression]), new(kind, value));
        Assert.Equal(matches ? "Success" : "NoMatch", result.Status);
        Assert.Equal(truth, Assert.Single(Assert.Single(result.Trace).Cells).Truth);
    }

    [Theory]
    [InlineData("\"a,b[..]\"", "a,b[..]")]
    [InlineData("\"quote: \\\" slash: \\\\\"", "quote: \" slash: \\")]
    [InlineData("\"a\\'b\"", "a'b")]
    [InlineData("\"a\\nb\\rc\\t\"", "a\nb\rc\t")]
    [InlineData("\"\\u0041\\U01F600\"", "A😀")]
    public void Feel_strings_are_tokenized_and_decoded_once(string expression, string expected)
    {
        var result = Evaluate(Table([expression], inputType: "string", outputTexts: [expression], outputType: "string"), new("string", expected));
        Assert.Equal("Success", result.Status);
        Assert.Equal(expected, result.Result.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("UNIQUE", "HitPolicyViolation", 0)]
    [InlineData("ANY", "Success", 2)]
    [InlineData("FIRST", "Success", 1)]
    [InlineData("RULE ORDER", "Success", 2)]
    [InlineData("COLLECT", "Success", 2)]
    public void Every_supported_hit_policy_reports_actual_matches_and_selections(string policy, string status, int selected)
    {
        var result = Evaluate(Table(["> 0", ">= 1"], policy), new("number", "1"));
        Assert.Equal(status, result.Status);
        Assert.All(result.Trace, trace => Assert.True(trace.Matched));
        Assert.Equal(selected, result.Trace.Count(trace => trace.Selected));
        if (policy is "RULE ORDER" or "COLLECT") Assert.Equal(2, result.Result.GetArrayLength());
    }

    [Fact]
    public void Any_rejects_conflicting_results_and_accepts_exact_equivalent_numbers()
    {
        var conflict = Evaluate(Table(["-", "-"], "ANY", ["1", "2"]), new("number", "1"));
        Assert.Equal("HitPolicyViolation", conflict.Status);
        Assert.Equal("AnyConflict", Assert.Single(conflict.Diagnostics).Code);
        var same = Evaluate(Table(["-", "-"], "ANY", ["1.0", "1.00"]), new("number", "1"));
        Assert.Equal("Success", same.Status);
        Assert.Equal("1", same.Result.GetProperty("value").GetString());
    }

    [Fact]
    public void Multiple_output_shape_is_named_and_policy_lists_preserve_document_order()
    {
        var doc = XDocument.Parse(Table(["-", "-"], "RULE ORDER", ["1", "2"]));
        XNamespace ns = DecisionNamespace;
        var table = doc.Descendants(ns + "decisionTable").Single();
        table.Elements(ns + "output").Single().SetAttributeValue("name", "approval level");
        table.Elements(ns + "output").Last().AddAfterSelf(new XElement(ns + "output", new XAttribute("id", "out2"), new XAttribute("name", "true"), new XAttribute("typeRef", "string")));
        var index = 0;
        foreach (var row in table.Elements(ns + "rule"))
            row.Add(new XElement(ns + "outputEntry", new XAttribute("id", "extra" + index), new XElement(ns + "text", "\"" + index++ + "\"")));
        var result = Evaluate(doc.ToString(), new("number", "1"));
        Assert.Equal("Success", result.Status);
        Assert.Equal("1", result.Result[0].GetProperty("approval level").GetProperty("value").GetString());
        Assert.Equal("1", result.Result[1].GetProperty("true").GetProperty("value").GetString());
    }

    [Fact]
    public void No_match_is_distinct_from_matching_null_for_every_policy()
    {
        foreach (var policy in new[] { "UNIQUE", "ANY", "FIRST", "RULE ORDER", "COLLECT" })
        {
            var noMatch = Evaluate(Table(["0"], policy), new("number", "1"));
            var nullResult = Evaluate(Table(["-"], policy, ["null"]), new("number", "1"));
            Assert.Equal("NoMatch", noMatch.Status);
            Assert.Equal("null", noMatch.Result.GetProperty("kind").GetString());
            Assert.Equal("Success", nullResult.Status);
        }
    }

    [Theory]
    [InlineData("= 1")]
    [InlineData("!= 1")]
    [InlineData("not(not(1))")]
    [InlineData("[2..1]")]
    [InlineData("1e3")]
    [InlineData("12345678901234567890123456789012345")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("+1")]
    [InlineData("1.")]
    [InlineData(" ")]
    [InlineData("DateTime.Parse(\"2026-11-02\")")]
    [InlineData("\"x\".GetType()")]
    [InlineData("\"\\b\"")]
    [InlineData("\"\\f\"")]
    [InlineData("\"\\/\"")]
    [InlineData("\"\\uD83D\\uDE00\"")]
    [InlineData("\"\\U110000\"")]
    [InlineData("\"\\q\"")]
    [InlineData("\"unterminated")]
    public void Unsupported_or_invalid_cells_are_refused_before_execution(string expression)
    {
        var compilation = engine.Compile(Table(["-", expression], "FIRST"));
        Assert.False(compilation.Success);
        Assert.Null(compilation.Artifact);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.Contains(compilation.Diagnostics, d => d.RuleId == "r1" && d.ClauseId == "input");
        if (expression.Trim().Length > 1) Assert.All(compilation.Diagnostics, diagnostic => Assert.DoesNotContain(expression, diagnostic.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("not")]
    [InlineData("for")]
    [InlineData("amount.total")]
    [InlineData("Amount Total")]
    public void Reserved_or_non_profile_input_symbols_are_rejected(string symbol)
    {
        Assert.False(engine.Compile(Table(["-"], inputName: symbol)).Success);
    }

    [Fact]
    public void Valid_similar_names_remain_case_sensitive()
    {
        Assert.True(engine.Compile(Table(["-"], inputName: "trueValue")).Success);
        var artifact = Compile(Table(["-"], inputName: "True"));
        Assert.Equal("InvalidInput", engine.Evaluate(artifact, new Dictionary<string, DecisionValue> { ["true"] = new("number", "1") }).Status);
        Assert.Equal("Success", engine.Evaluate(artifact, new Dictionary<string, DecisionValue> { ["True"] = new("number", "1") }).Status);
    }

    [Theory]
    [InlineData("PRIORITY")]
    [InlineData("OUTPUT ORDER")]
    [InlineData("bogus")]
    public void Unsupported_policies_are_not_silently_downgraded(string policy) => Assert.False(engine.Compile(Table(["-"], policy)).Success);

    [Theory]
    [InlineData("aggregation", "SUM")]
    [InlineData("preferredOrientation", "Rule-as-Column")]
    [InlineData("unknownSemantic", "x")]
    public void Unknown_or_unsupported_semantic_attributes_are_refused(string name, string value)
    {
        var doc = XDocument.Parse(Table(["-"]));
        doc.Descendants(XName.Get("decisionTable", DecisionNamespace)).Single().SetAttributeValue(name, value);
        Assert.False(engine.Compile(doc.ToString()).Success);
    }

    [Fact]
    public void Unsupported_elements_languages_and_ambiguous_extensions_are_refused()
    {
        XNamespace ns = DecisionNamespace;
        foreach (var change in new Action<XDocument>[]
        {
            d => d.Root!.Add(new XElement(ns + "import")),
            d => d.Descendants(ns + "output").Single().Add(new XElement(ns + "defaultOutputEntry", new XElement(ns + "text", "1"))),
            d => d.Descendants(ns + "input").Single().Add(new XElement(ns + "inputValues", new XElement(ns + "text", "1"))),
            d => d.Root!.SetAttributeValue("expressionLanguage", "javascript"),
            d => d.Root!.Add(new XElement(ns + "extensionElements", new XElement(XName.Get("execute", "urn:unknown")))),
            d => d.Root!.Add(new XElement(ns + "decision", new XAttribute("id", "another")))
        })
        {
            var doc = XDocument.Parse(Table(["-"])); change(doc);
            Assert.False(engine.Compile(doc.ToString()).Success);
        }
    }

    [Fact]
    public void Xml_external_entities_malformed_duplicate_ids_and_wrong_width_never_compile()
    {
        foreach (var xml in new[]
        {
            "<!DOCTYPE definitions [<!ENTITY evil SYSTEM 'file:///etc/passwd'>]>" + Table(["-"]),
            Table(["-"]).Replace("id=\"r0\"", "id=\"input\"", StringComparison.Ordinal),
            Table(["-"]).Replace("<text>-</text>", "<text>", StringComparison.Ordinal),
            Table(["-"]).Replace("<inputEntry", "<outputEntry", StringComparison.Ordinal).Replace("</inputEntry>", "</outputEntry>", StringComparison.Ordinal),
            "<broken>", new string('x', 1_048_577)
        }) Assert.False(engine.Compile(xml).Success);
    }

    [Fact]
    public void Nonsemantic_formatting_and_diagrams_do_not_change_executable_identity()
    {
        var original = Table(["1.00"]);
        var doc = XDocument.Parse(original);
        XNamespace dmndi = "https://www.omg.org/spec/DMN/20191111/DMNDI/";
        doc.Root!.Add(new XElement(dmndi + "DMNDI", new XElement(dmndi + "DMNDiagram", new XAttribute("id", "diagram"))));
        var first = Compile(original); var second = Compile(doc.ToString());
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.SourceHash, second.SourceHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original))), first.SourceHash);
    }

    [Fact]
    public void Missing_wrong_extra_malformed_and_oversized_inputs_fail_without_coercion()
    {
        var artifact = Compile(Table(["-"]));
        foreach (var values in new Dictionary<string, DecisionValue>[]
        {
            [], new() { ["wrong"] = new("number", "1") }, new() { ["amount"] = new("string", "1") },
            new() { ["amount"] = new("number", "NaN") }, new() { ["amount"] = new("number", "1e3") },
            new() { ["amount"] = new("boolean", "True") }, new() { ["amount"] = new("null", "null") },
            new() { ["amount"] = new("number", "1"), ["extra"] = new("number", "1") }
        }) Assert.Equal("InvalidInput", engine.Evaluate(artifact, values).Status);
        Assert.Equal("InvalidInput", Evaluate(Table(["-"], inputType: "string"), new("string", new string('x', 4097))).Status);
    }

    [Fact]
    public void Bounds_and_cancellation_never_return_partial_success()
    {
        var artifact = Compile(Table(["-", "-"], "FIRST"));
        var low = engine.Evaluate(artifact, Inputs(new("number", "1")), 1);
        Assert.Equal("BudgetExceeded", low.Status);
        Assert.Equal(JsonValueKind.Null, low.Result.ValueKind);
        Assert.True(low.Operations <= 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Equal("Cancelled", engine.Evaluate(artifact, Inputs(new("number", "1")), cancellationToken: cancellation.Token).Status);
        Assert.Equal("Cancelled", Assert.Single(engine.Compile(Table(["-"]), cancellation.Token).Diagnostics).Code);
        Assert.False(engine.Compile(Table([string.Join(',', Enumerable.Repeat("1", 129))])).Success);
        Assert.False(engine.Compile(Table(Enumerable.Repeat("-", 1001).ToArray())).Success);
        Assert.False(engine.Compile(Table([new string('1', 4097)])).Success);
    }

    [Fact]
    public void Artifact_tampering_unknown_semantics_and_noncanonical_material_are_refused()
    {
        var artifact = Compile(Table(["-"]));
        DecisionArtifact Change(byte[] bytes, string? hash = null, string? semantics = null) => new(artifact.DecisionId, artifact.SourceHash, hash ?? artifact.ContentHash, artifact.ProfileId, semantics ?? artifact.SemanticsId, bytes);
        var modified = artifact.ExecutableBytes; modified[^4] ^= 1;
        Assert.Equal("Unsupported", engine.Evaluate(Change(modified), Inputs(new("number", "1"))).Status);
        Assert.Equal("Unavailable", engine.Evaluate(Change(artifact.ExecutableBytes, semantics: "future"), Inputs(new("number", "1"))).Status);
        var whitespace = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(artifact.ExecutableBytes) + " ");
        Assert.Equal("Unsupported", engine.Evaluate(Change(whitespace, Convert.ToHexStringLower(SHA256.HashData(whitespace))), Inputs(new("number", "1"))).Status);
        var returned = artifact.ExecutableBytes; returned[0] = 0;
        Assert.Equal("Success", engine.Evaluate(artifact, Inputs(new("number", "1"))).Status);
    }

    [Fact]
    public void All_public_result_and_artifact_contracts_survive_default_and_web_json_roundtrips()
    {
        var compiled = engine.Compile(Table(["-"]));
        var evaluation = engine.Evaluate(compiled.Artifact!, Inputs(new("number", "1")));
        foreach (var options in new[] { new JsonSerializerOptions(), new JsonSerializerOptions(JsonSerializerDefaults.Web) })
        {
            var copiedCompilation = JsonSerializer.Deserialize<DecisionCompilation>(JsonSerializer.Serialize(compiled, options), options)!;
            Assert.True(copiedCompilation.Success);
            Assert.Equal("Success", engine.Evaluate(copiedCompilation.Artifact!, Inputs(new("number", "1"))).Status);
            var copiedEvaluation = JsonSerializer.Deserialize<DecisionEvaluation>(JsonSerializer.Serialize(evaluation, options), options)!;
            Assert.Equal(evaluation.Result.GetRawText(), copiedEvaluation.Result.GetRawText());
            Assert.Equal("r0", Assert.Single(copiedEvaluation.Trace).RuleId);
            Assert.Equal("true", Assert.Single(Assert.Single(copiedEvaluation.Trace).Cells).Truth);
        }
    }

    [Fact]
    public void Evaluation_is_independent_of_current_culture_and_parallel_callers()
    {
        var artifact = Compile(Table(["0.1"]));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            Assert.Equal("Success", engine.Evaluate(artifact, Inputs(new("number", "0.1"))).Status);
            Assert.All(Enumerable.Range(0, 16).AsParallel().Select(_ => engine.Evaluate(artifact, Inputs(new("number", "0.10")))), result => Assert.Equal("Success", result.Status));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Feature_is_scoped_preserves_explicit_replacements_and_rejects_duplicates()
    {
        var services = new ServiceCollection(); new DecisionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IDecisionCompiler>(), second.ServiceProvider.GetRequiredService<IDecisionCompiler>());
        var replacement = new DecisionEngine();
        var custom = new ServiceCollection(); custom.AddSingleton<IDecisionCompiler>(replacement); new DecisionsFeature().ConfigureServices(custom);
        using var customProvider = custom.BuildServiceProvider(); Assert.Same(replacement, customProvider.GetRequiredService<IDecisionCompiler>());
        custom.AddSingleton<IDecisionCompiler>(new DecisionEngine());
        Assert.Throws<InvalidOperationException>(() => new DecisionsFeature().ConfigureServices(custom));
    }

    [Theory]
    [InlineData(typeof(IDecisionCompiler), false)]
    [InlineData(typeof(IDecisionEvaluator), false)]
    [InlineData(typeof(IDecisionCompiler), true)]
    [InlineData(typeof(IDecisionEvaluator), true)]
    public async Task Shell_startup_refuses_late_duplicate_or_removed_evaluation_contracts(Type contract, bool remove)
    {
        var services = new ServiceCollection();
        new DecisionsFeature().ConfigureServices(services);
        if (remove) services.RemoveAll(contract);
        else services.AddScoped(contract, typeof(DecisionEngine));
        using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var initializer in provider.GetServices<IShellInitializer>())
                await initializer.InitializeAsync(CancellationToken.None);
        });
        Assert.Contains(contract.FullName!, exception.Message);
        Assert.Contains("exactly one", exception.Message);
    }

    [Fact]
    public async Task Shell_startup_accepts_one_host_replacement_registered_before_or_after_the_feature()
    {
        var services = new ServiceCollection();
        var compiler = new DecisionEngine(); var evaluator = new DecisionEngine();
        services.AddSingleton<IDecisionCompiler>(compiler);
        var feature = new DecisionsFeature(); feature.ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<IDecisionEvaluator>(evaluator));
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>());
        await initializer.InitializeAsync(CancellationToken.None);
        var compiled = provider.GetRequiredService<IDecisionCompiler>().Compile(Table(["-"]));
        Assert.True(compiled.Success);
        Assert.Same(compiler, provider.GetRequiredService<IDecisionCompiler>());
        Assert.Same(evaluator, provider.GetRequiredService<IDecisionEvaluator>());
        Assert.Equal("Success", provider.GetRequiredService<IDecisionEvaluator>().Evaluate(compiled.Artifact!, Inputs(new("number", "1"))).Status);
    }

    [Fact]
    public void Inert_diagram_ids_still_participate_in_global_uniqueness()
    {
        var doc = XDocument.Parse(Table(["-"]));
        XNamespace diagram = "https://www.omg.org/spec/DMN/20191111/DMNDI/";
        doc.Root!.Add(new XElement(diagram + "DMNDI", new XElement(diagram + "DMNDiagram", new XAttribute("id", "input"))));
        Assert.Contains(engine.Compile(doc.ToString()).Diagnostics, d => d.Code == "InvalidOrDuplicateId");
    }

    [Fact]
    public void Input_data_references_and_single_output_contract_are_checked()
    {
        XNamespace ns = DecisionNamespace;
        var doc = XDocument.Parse(Table(["-"]));
        doc.Root!.AddFirst(new XElement(ns + "inputData", new XAttribute("id", "external"), new XAttribute("name", "amount"),
            new XElement(ns + "variable", new XAttribute("id", "externalVariable"), new XAttribute("name", "amount"), new XAttribute("typeRef", "number"))));
        var decision = doc.Descendants(ns + "decision").Single();
        decision.AddFirst(new XElement(ns + "informationRequirement", new XAttribute("id", "dependency"), new XElement(ns + "requiredInput", new XAttribute("href", "#external"))));
        Assert.True(engine.Compile(doc.ToString()).Success);
        doc.Descendants(ns + "requiredInput").Single().SetAttributeValue("href", "#missing");
        Assert.Contains(engine.Compile(doc.ToString()).Diagnostics, d => d.Code == "UnsupportedDependency");
        doc.Descendants(ns + "requiredInput").Single().SetAttributeValue("href", "#external");
        decision.Add(new XElement(ns + "variable", new XAttribute("id", "resultVariable"), new XAttribute("name", "approval"), new XAttribute("typeRef", "string")));
        Assert.Contains(engine.Compile(doc.ToString()).Diagnostics, d => d.Code == "DecisionVariableType");
    }

    [Fact]
    public void Duplicate_semantic_singletons_are_not_silently_discarded()
    {
        XNamespace ns = DecisionNamespace;
        var doc = XDocument.Parse(Table(["-"]));
        var decision = doc.Descendants(ns + "decision").Single();
        decision.Add(new XElement(ns + "variable", new XAttribute("id", "v1"), new XAttribute("name", "approval")));
        decision.Add(new XElement(ns + "variable", new XAttribute("id", "v2"), new XAttribute("name", "approval")));
        Assert.Contains(engine.Compile(doc.ToString()).Diagnostics, d => d.Code == "DuplicateDecisionVariable");
    }

    [Fact]
    public void Xml_depth_and_element_limits_precede_semantic_admission()
    {
        var deep = "<a>" + string.Concat(Enumerable.Repeat("<b>", 65)) + string.Concat(Enumerable.Repeat("</b>", 65)) + "</a>";
        Assert.Contains(engine.Compile(deep).Diagnostics, d => d.Code == "DocumentLimitExceeded");
        var wide = "<a>" + string.Concat(Enumerable.Repeat("<x/>", 100_001)) + "</a>";
        Assert.Contains(engine.Compile(wide).Diagnostics, d => d.Code == "DocumentLimitExceeded");
    }

    [Fact]
    public void Invalid_scalar_encoding_missing_input_and_output_type_have_specific_outcomes()
    {
        var artifact = Compile(Table(["-"]));
        Assert.Equal("MissingInput", Assert.Single(engine.Evaluate(artifact, new Dictionary<string, DecisionValue>()).Diagnostics).Code);
        Assert.Equal("InvalidInput", Evaluate(Table(["-"], inputType: "string"), new("string", new string((char)0xD800, 1))).Status);
        Assert.Contains(engine.Compile(Table(["-"], outputTexts: ["true"])).Diagnostics, d => d.Code == "OutputTypeMismatch");
        var boolResult = Evaluate(Table(["true"], inputType: "boolean", outputTexts: ["false"], outputType: "boolean"), new("boolean", "true"));
        Assert.Equal("Success", boolResult.Status);
        Assert.Equal("false", boolResult.Result.GetProperty("value").GetString());
    }

    [Fact]
    public void Constructor_byte_and_trace_input_mutations_cannot_change_frozen_values()
    {
        var artifact = Compile(Table(["-"]));
        var bytes = artifact.ExecutableBytes;
        var snapshot = new DecisionArtifact(artifact.DecisionId, artifact.SourceHash, artifact.ContentHash, artifact.ProfileId, artifact.SemanticsId, bytes);
        bytes[0] = 0;
        Assert.Equal("Success", engine.Evaluate(snapshot, Inputs(new("number", "1"))).Status);
        var cells = new List<DecisionCellTrace> { new("input", "true") };
        var trace = new DecisionRuleTrace("r0", true, true, cells); cells.Clear();
        Assert.Single(trace.Cells);
    }

    [Fact]
    public void One_leading_utf8_signature_preserves_original_source_hash_and_executable_meaning()
    {
        var xml = Table(["-"], outputTexts: ["\"original value\""], outputType: "string");
        var original = Compile(xml);
        var signedXml = "\uFEFF" + xml;
        var signed = Compile(signedXml);
        Assert.NotEqual(original.SourceHash, signed.SourceHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signedXml))), signed.SourceHash);
        Assert.Equal(original.ContentHash, signed.ContentHash);
        Assert.Equal("original value", engine.Evaluate(signed, Inputs(new("number", "1"))).Result.GetProperty("value").GetString());
        Assert.False(engine.Compile("\uFEFF\uFEFF" + xml).Success);
    }

    [Fact]
    public void Single_output_name_is_optional_and_does_not_change_scalar_executable_identity()
    {
        XNamespace ns = DecisionNamespace;
        var namedXml = Table(["-"], outputTexts: ["\"Approved\""], outputType: "string");
        var unnamedDocument = XDocument.Parse(namedXml);
        unnamedDocument.Descendants(ns + "output").Single().Attribute("name")!.Remove();
        var unnamedXml = unnamedDocument.ToString(SaveOptions.DisableFormatting);
        var named = Compile(namedXml);
        var unnamed = Compile(unnamedXml);
        Assert.NotEqual(named.SourceHash, unnamed.SourceHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(unnamedXml))), unnamed.SourceHash);
        Assert.Equal(named.ContentHash, unnamed.ContentHash);
        var canonical = XDocument.Parse(Encoding.UTF8.GetString(named.ExecutableBytes));
        Assert.Null(canonical.Descendants(ns + "output").Single().Attribute("name"));
        Assert.Equal("output", canonical.Descendants(ns + "output").Single().Attribute("id")!.Value);
        var result = engine.Evaluate(unnamed, Inputs(new("number", "1")));
        Assert.Equal("Success", result.Status);
        Assert.Equal("Approved", result.Result.GetProperty("value").GetString());
        Assert.Equal("Success", engine.Evaluate(JsonSerializer.Deserialize<DecisionArtifact>(JsonSerializer.Serialize(unnamed))!, Inputs(new("number", "1"))).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("result")]
    public void Multiple_outputs_require_distinct_nonempty_names(string? secondName)
    {
        XNamespace ns = DecisionNamespace;
        var doc = XDocument.Parse(Table(["-"]));
        var output = doc.Descendants(ns + "output").Single();
        var second = new XElement(ns + "output", new XAttribute("id", "second_output"), new XAttribute("typeRef", "number"));
        if (secondName is not null) second.SetAttributeValue("name", secondName);
        output.AddAfterSelf(second);
        doc.Descendants(ns + "rule").Single().Add(new XElement(ns + "outputEntry", new XAttribute("id", "second_value"), new XElement(ns + "text", "2")));
        Assert.Contains(engine.Compile(doc.ToString()).Diagnostics, diagnostic => diagnostic.Code == (secondName == "result" ? "DuplicateOutputName" : "InvalidOutputName"));
    }

    private DecisionArtifact Compile(string xml)
    {
        var result = engine.Compile(xml);
        Assert.True(result.Success, string.Join(';', result.Diagnostics.Select(d => d.Code + ":" + d.ClauseId)));
        return result.Artifact!;
    }
    private DecisionEvaluation Evaluate(string xml, DecisionValue value) => engine.Evaluate(Compile(xml), Inputs(value));
    private static Dictionary<string, DecisionValue> Inputs(DecisionValue value) => new(StringComparer.Ordinal) { ["amount"] = value };
    private const string DecisionNamespace = "https://www.omg.org/spec/DMN/20191111/MODEL/";

    private static string Table(string[] tests, string policy = "UNIQUE", string[]? outputTexts = null, string inputType = "number", string outputType = "number", string inputName = "amount")
    {
        XNamespace ns = DecisionNamespace;
        return new XElement(ns + "definitions", new XAttribute("id", "defs"), new XAttribute("name", "Example"), new XAttribute("namespace", "urn:example"),
            new XElement(ns + "decision", new XAttribute("id", "approval"), new XAttribute("name", "Approval"),
                new XElement(ns + "decisionTable", new XAttribute("id", "table"), new XAttribute("hitPolicy", policy),
                    new XElement(ns + "input", new XAttribute("id", "input"), new XElement(ns + "inputExpression", new XAttribute("id", "expression"), new XAttribute("typeRef", inputType), new XElement(ns + "text", inputName))),
                    new XElement(ns + "output", new XAttribute("id", "output"), new XAttribute("name", "result"), new XAttribute("typeRef", outputType)),
                    tests.Select((test, i) => new XElement(ns + "rule", new XAttribute("id", "r" + i),
                        new XElement(ns + "inputEntry", new XAttribute("id", "test" + i), new XElement(ns + "text", test)),
                        new XElement(ns + "outputEntry", new XAttribute("id", "value" + i), new XElement(ns + "text", outputTexts?[i] ?? "1"))))))).ToString(SaveOptions.DisableFormatting);
    }
}
