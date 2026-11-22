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
        var refusal = Assert.Single(result.Diagnostics.Where(d => d.IsError && d.Path.EndsWith("." + member, StringComparison.Ordinal)));
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
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Path.EndsWith("." + member, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Elsa.SendHttpRequest")]
    [InlineData("Elsa.FlowSendHttpRequest")]
    public async Task Blank_content_type_with_nonempty_string_content_selects_the_legacy_JSON_writer(string type)
    {
        var result = await Convert(type, "contentType", Literal(" ").GetRawText(), "hello");
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("application/json", Text(Input(Node(result.Value!.State, "subject"), "ContentType")));
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
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Path.EndsWith("." + member, StringComparison.Ordinal));
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
