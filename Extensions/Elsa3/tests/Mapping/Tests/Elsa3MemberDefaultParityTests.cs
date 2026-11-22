using System.Text.Json;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3MemberDefaultParityTests
{
    public static IEnumerable<object[]> DefaultShapes()
    {
        foreach (var shape in new[] { "absent", "null", "{}", "{\"typeName\":\"System.Object\"}", "{\"typeName\":\"System.Object\",\"expression\":{}}" })
        {
            yield return ["Elsa.For", "outerBoundInclusive", "EndInclusive", shape, "true"];
            yield return ["Elsa.Delay", "timeSpan", "Duration", shape, "00:01:00"];
            yield return ["Elsa.Timer", "interval", "Interval", shape, "00:01:00"];
            yield return ["Elsa.FlowSendHttpRequest", "expectedStatusCodes", "ExpectedStatusCodes", shape, "[200]"];
        }
    }

    [Theory, MemberData(nameof(DefaultShapes))]
    public async Task Defaulted_wrappers_keep_the_selected_legacy_default(string type, string member, string key, string shape, string expected)
    {
        var result = await Convert(type, member, shape);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), key)));
    }

    [Theory]
    [InlineData("Elsa.For", "outerBoundInclusive", "EndInclusive", "false")]
    [InlineData("Elsa.Delay", "timeSpan", "Duration", "00:00:00")]
    public async Task Deliberately_cleared_value_types_do_not_acquire_declared_defaults(string type, string member, string key, string expected)
    {
        foreach (var shape in new[] { "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":null}}", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", Literal(" ").GetRawText() })
        {
            var result = await Convert(type, member, shape);
            Assert.True(result.Succeeded, Describe(result.Diagnostics));
            Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), key)));
        }
    }

    public static IEnumerable<object[]> RefusedShapes()
    {
        foreach (var type in new[] { "Elsa.SendHttpRequest", "Elsa.FlowSendHttpRequest" })
        {
            foreach (var shape in new[] { "absent", "{}", "{\"typeName\":\"String\",\"expression\":{}}" })
                foreach (var content in new[] { "hello", "" })
                    yield return [type, "contentType", shape, content];
            yield return [type, "method", Literal(" ").GetRawText(), "unbound"];
        }
        foreach (var shape in new[] { "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":null}}", Literal("").GetRawText(), Literal("[]").GetRawText(), "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":[]}}", "[]", Literal("null").GetRawText() })
            yield return ["Elsa.FlowSendHttpRequest", "expectedStatusCodes", shape, "unbound"];
        foreach (var type in new[] { "Elsa.Timer", "Elsa.WriteHttpResponse" })
            foreach (var shape in new[] { "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", Literal(" ").GetRawText(), Literal("0").GetRawText(), Literal("-1").GetRawText(), Literal("invalid").GetRawText() })
                yield return [type, type == "Elsa.Timer" ? "interval" : "statusCode", shape, "unbound"];
        yield return ["Elsa.Timer", "interval", Literal("-00:00:01").GetRawText(), "unbound"];
        yield return ["Elsa.WriteHttpResponse", "statusCode", "{\"typeName\":\"Int32\",\"expression\":{\"type\":\"JavaScript\",\"value\":\"200\"}}", "unbound"];
        foreach (var type in new[] { "Elsa.Cron", "Elsa.RunJavaScript" })
            foreach (var shape in new[] { "absent", "{}", Literal("  ").GetRawText() })
                yield return [type, type == "Elsa.Cron" ? "cronExpression" : "script", shape, "unbound"];
    }

    [Theory, MemberData(nameof(RefusedShapes))]
    public async Task Incompatible_uses_are_refused_at_the_original_member(string type, string member, string shape, string content)
    {
        var result = await Convert(type, member, shape, content);
        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith("." + member, StringComparison.Ordinal));
        Assert.NotEqual(Elsa3ActivityDiagnosticCodes.RequiredInputUnbound, refusal.Code);
        Assert.Contains(member, refusal.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Guidance));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == Elsa3ActivityDiagnosticCodes.RequiredInputUnbound);
    }

    [Theory]
    [InlineData("Elsa.For", "outerBoundInclusive")]
    [InlineData("Elsa.Delay", "timeSpan")]
    [InlineData("Elsa.Timer", "interval")]
    [InlineData("Elsa.FlowSendHttpRequest", "expectedStatusCodes")]
    [InlineData("Elsa.WriteHttpResponse", "statusCode")]
    public async Task Invalid_expression_metadata_cannot_be_repaired_into_a_default(string type, string member)
    {
        foreach (var expression in new[] { "null", "1", "\"Literal\"", "{\"value\":null}", "{\"type\":null}", "{\"type\":\"Unknown\"}" })
        {
            var result = await Convert(type, member, "{\"typeName\":\"String\",\"expression\":" + expression + "}");
            Assert.False(result.Succeeded, expression);
            Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith("." + member, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Elsa.SendHttpRequest")]
    [InlineData("Elsa.FlowSendHttpRequest")]
    public async Task Blank_content_type_refuses_the_selected_legacy_JSON_header_difference(string type)
    {
        var result = await Convert(type, "contentType", Literal(" ").GetRawText(), "hello");
        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.EndsWith(".contentType", error.Path, StringComparison.Ordinal);
        Assert.Contains("charset", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Elsa.SendHttpRequest")]
    [InlineData("Elsa.FlowSendHttpRequest")]
    public async Task An_unproven_String_body_cannot_silently_lose_its_empty_body_headers(string type)
    {
        var subject = Subject(type, "content", Expression("JavaScript", "return '';", "String").GetRawText(), "unbound");
        subject.AdditionalProperties["contentType"] = Literal("application/json");
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", subject)));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith(".content", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("\"Literal\"")]
    public async Task A_memory_reference_does_not_repair_malformed_present_expression_metadata(string expression)
    {
        var node = Subject("Elsa.For", "outerBoundInclusive", "{\"typeName\":\"Boolean\",\"expression\":" + expression + ",\"memoryReference\":{\"id\":\"inclusive\"}}", "unbound");
        var variable = new Elsa3Variable { Id = "inclusive", Name = "inclusive", TypeName = "Boolean", Value = "true" };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node), variables: [variable]));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith(".outerBoundInclusive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_typed_Variable_expression_keeps_its_declaration_reference_contract()
    {
        var node = Subject("Elsa.For", "outerBoundInclusive", "{\"typeName\":\"Boolean\",\"expression\":{\"type\":\"Variable\",\"value\":{\"id\":\"inclusive\",\"name\":\"inclusive\"}}}", "unbound");
        var variable = new Elsa3Variable { Id = "inclusive", Name = "inclusive", TypeName = "Boolean", Value = "false" };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node), variables: [variable]));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("Variable", Input(Node(result.Value!.State, "subject"), "EndInclusive").Value.ExpressionType);
    }

    [Theory]
    [InlineData("Elsa.For", "outerBoundInclusive", "Boolean", "false")]
    [InlineData("Elsa.SendHttpRequest", "method", "String", "POST")]
    [InlineData("Elsa.FlowSendHttpRequest", "method", "String", "POST")]
    public async Task Memory_only_wrappers_cannot_replace_the_legacy_default_with_a_variable(string type, string member, string alias, string initial)
    {
        var node = Subject(type, member, JsonSerializer.Serialize(new { typeName = alias, memoryReference = new { id = "source" } }), "unbound");
        var variable = new Elsa3Variable { Id = "source", Name = "source", TypeName = alias, Value = initial };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node), variables: [variable]));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith("." + member, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Unknown", "false")]
    [InlineData("Unknown", "1")]
    [InlineData("", "false")]
    [InlineData("", "1")]
    [InlineData("JavaScript", "false")]
    [InlineData("Liquid", "1")]
    public async Task Non_string_unknown_or_script_expression_payloads_are_refused_at_the_member(string syntax, string payload)
    {
        var result = await Convert("Elsa.For", "outerBoundInclusive", "{\"typeName\":\"Boolean\",\"expression\":{\"type\":" + JsonSerializer.Serialize(syntax) + ",\"value\":" + payload + "}}");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith(".outerBoundInclusive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Literal_value_takes_precedence_over_ignored_serialized_memory_metadata()
    {
        var node = Subject("Elsa.For", "outerBoundInclusive", "{\"typeName\":\"Boolean\",\"expression\":{\"type\":\"Literal\",\"value\":false},\"memoryReference\":{\"id\":\"inclusive\"}}", "unbound");
        var variable = new Elsa3Variable { Id = "inclusive", Name = "inclusive", TypeName = "Boolean", Value = "true" };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node), variables: [variable]));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var input = Input(Node(result.Value!.State, "subject"), "EndInclusive");
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Equal("false", Text(input));
    }

    [Theory]
    [InlineData("\"extra\":1,\"extra\":2")]
    [InlineData("\"memoryReference\":{\"id\":\"another-ignored-reference\"}")]
    [InlineData("\"expression\":{\"type\":\"Literal\",\"value\":true},\"expression\":{\"type\":\"Literal\",\"value\":false}")]
    public async Task Removing_ignored_memory_metadata_keeps_last_property_semantics_without_throwing(string extra)
    {
        var shape = "{\"typeName\":\"Boolean\",\"expression\":{\"type\":\"Literal\",\"value\":false},\"memoryReference\":{\"id\":\"ignored\"}," + extra + "}";
        var result = await Convert("Elsa.For", "outerBoundInclusive", shape);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var input = Input(Node(result.Value!.State, "subject"), "EndInclusive");
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Equal("false", Text(input));
    }

    [Theory]
    [InlineData("{\"expression\":{\"type\":\"Literal\",\"value\":\"false\"}}")]
    [InlineData("{\"expression\":{\"type\":\"Literal\",\"value\":\"false\"},\"memoryReference\":{\"id\":\"inclusive\"}}")]
    [InlineData("{\"memoryReference\":{\"id\":\"inclusive\"}}")]
    public async Task A_nonempty_wrapper_without_type_metadata_cannot_become_a_binding(string shape)
    {
        var node = Subject("Elsa.For", "outerBoundInclusive", shape, "unbound");
        var variable = new Elsa3Variable { Id = "inclusive", Name = "inclusive", TypeName = "Boolean", Value = "true" };
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node), variables: [variable]));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith(".outerBoundInclusive", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":null}}")]
    [InlineData("{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}")]
    [InlineData("{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\",\"value\":\" \"}}")]
    public async Task Every_cleared_status_list_explains_the_nonempty_list_and_Done_path_review(string shape)
    {
        var result = await Convert("Elsa.FlowSendHttpRequest", "expectedStatusCodes", shape);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Contains("nonempty", diagnostic.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Done/status", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Created", "201")]
    [InlineData("202", "202")]
    [InlineData("599", "599")]
    public async Task Supported_positive_response_status_is_retained(string source, string expected)
    {
        var result = await Convert("Elsa.WriteHttpResponse", "statusCode", Literal(source).GetRawText());
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), "StatusCode")));
    }

    [Theory]
    [InlineData("Elsa.Timer", "interval", "00:00:01", "Interval", "00:00:01")]
    [InlineData("Elsa.FlowSendHttpRequest", "expectedStatusCodes", "[200,404]", "ExpectedStatusCodes", "[200,404]")]
    [InlineData("Elsa.For", "outerBoundInclusive", "false", "EndInclusive", "false")]
    [InlineData("Elsa.Delay", "timeSpan", "00:00:00", "Duration", "00:00:00")]
    public async Task Explicit_supported_values_remain_authored_values(string type, string member, string text, string key, string expected)
    {
        var result = await Convert(type, member, Literal(text, "Object").GetRawText());
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), key)));
    }

    [Theory]
    [InlineData("Elsa.For", "outerBoundInclusive")]
    [InlineData("Elsa.Delay", "timeSpan")]
    public async Task Authored_null_text_and_empty_collections_cannot_become_declared_defaults(string type, string member)
    {
        foreach (var text in new[] { "null", "[]", "{}" })
            Assert.False((await Convert(type, member, Literal(text).GetRawText())).Succeeded);
    }

    [Theory]
    [InlineData("Elsa.Delay", "timeSpan", "Duration", "{\"typeName\":\"TimeSpan\",\"expression\":{\"type\":\"Literal\",\"value\":null}}", "00:01:00")]
    [InlineData("Elsa.Timer", "interval", "Interval", "{\"typeName\":\"TimeSpan\",\"expression\":{\"type\":\"Literal\",\"value\":\"00:00:00\"}}", "00:00:00")]
    [InlineData("Elsa.WriteHttpResponse", "statusCode", "StatusCode", "{\"typeName\":\"Int32\",\"expression\":{\"type\":\"Literal\",\"value\":\"0\"}}", "0")]
    public async Task Custom_maps_without_corrections_keep_the_generic_default_enum_and_interval_contracts(string type, string member, string key, string shape, string expected)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Subject(type, member, shape, "unbound"))), feature: new CustomMapFeature());
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), key)));
    }

    [Fact]
    public async Task Source_keys_remain_case_insensitive_and_the_callers_JSON_is_unchanged()
    {
        var subject = Subject("Elsa.Delay", "TiMeSpAn", "{\"typeName\":\"Object\",\"expression\":{\"type\":\"Literal\"}}", "unbound");
        var before = JsonSerializer.Serialize(subject);
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", subject)));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("00:00:00", Text(Input(Node(result.Value!.State, "subject"), "Duration")));
        Assert.Equal(before, JsonSerializer.Serialize(subject));
    }

    [Fact]
    public async Task Duplicate_defaulted_bindings_and_distinct_member_errors_remain_visible()
    {
        var subject = Subject("Elsa.FlowSendHttpRequest", "contentType", "{}", "hello");
        subject.AdditionalProperties["method"] = Literal(" ");
        subject.AdditionalProperties["expectedStatusCodes"] = Literal("[]");
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", subject)));
        foreach (var member in new[] { "contentType", "method", "expectedStatusCodes" })
            Assert.Contains(result.Diagnostics, d => d.IsError && (d.Path ?? string.Empty).EndsWith("." + member, StringComparison.Ordinal));
        var duplicate = Subject("Elsa.Delay", "timeSpan", "{}", "unbound");
        duplicate.AdditionalProperties["TimeSpan"] = Literal("00:00:01");
        var dup = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", duplicate)));
        Assert.Contains(dup.Diagnostics, d => d.Code == Elsa3ActivityDiagnosticCodes.DuplicateInputBinding);
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> Convert(string type, string member, string shape, string content = "unbound") =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", Subject(type, member, shape, content))));

    private static Elsa3Activity Subject(string type, string member, string shape, string content)
    {
        var node = Activity("subject", type);
        if (type.Contains("HttpRequest", StringComparison.Ordinal)) node.AdditionalProperties["url"] = Literal("https://example.org");
        if (content != "unbound") node.AdditionalProperties["content"] = Literal(content);
        if (shape != "absent") node.AdditionalProperties[member] = JsonDocument.Parse(shape).RootElement.Clone();
        if (type is "Elsa.Timer" or "Elsa.Cron") node.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        return node;
    }

    private sealed class CustomMapFeature : Elsa3MappingFeature
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IElsa3ActivityMap, CustomMap>();
            base.ConfigureServices(services);
        }
    }

    private sealed class CustomMap : IElsa3ActivityMap
    {
        public IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; } = new Elsa3BuiltInActivityMap().Entries.Select(entry => entry with
        {
            Members = entry.Members.Select(rule => rule with
            {
                OnAbsent = null, OnBlank = null, OnLiteralNull = null,
                Contract = rule.Contract switch
                {
                    Elsa3ValueContract.PositiveHttpResponseStatus => Elsa3ValueContract.EnumNameToInt,
                    Elsa3ValueContract.PositiveLiteralTimeSpan => Elsa3ValueContract.LiteralTimeSpanToString,
                    _ => rule.Contract
                }
            }).ToArray()
        }).ToArray();
        public Elsa3ActivityMapEntry? Find(string type) => Entries.FirstOrDefault(e => e.Elsa3TypeName == type);
    }
}
