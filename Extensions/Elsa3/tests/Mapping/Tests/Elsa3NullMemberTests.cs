using System.Text.Json;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 267 (#3200): a JSON <c>null</c> member converts to what the Elsa 3 runtime ran the activity with, one case per row
/// of the spec's per-member table. Where Elsa 3 ran with null and Elsa 4 accepts null, a typed null; where Elsa 3 ran with
/// a value Elsa 4's default equals, no binding; where they differ, Elsa 3's value; otherwise a refusal naming the member.
/// </summary>
public sealed class Elsa3NullMemberTests
{
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    /// <summary>Elsa 3's deliberate null: an input holding a literal null.</summary>
    private static readonly JsonElement LiteralNull =
        JsonSerializer.SerializeToElement(new { typeName = "String", expression = new { type = "Literal", value = (string?)null } });

    [Theory]
    [InlineData("Elsa.RunJavaScript", "possibleOutcomes", "possibleOutcomes", "script=return 1;")]
    [InlineData("Elsa.Fault", "message", "message", "")]
    [InlineData("Elsa.HttpEndpoint", "supportedMethods", "SupportedMethods", "path=/orders")]
    [InlineData("Elsa.HttpEndpoint", "policy", "Policy", "path=/orders")]
    [InlineData("Elsa.HttpEndpoint", "requestTimeout", "RequestTimeout", "path=/orders")]
    [InlineData("Elsa.HttpEndpoint", "requestSizeLimit", "RequestSizeLimit", "path=/orders")]
    [InlineData("Elsa.SendHttpRequest", "content", "Content", "url=https://example.org")]
    [InlineData("Elsa.SendHttpRequest", "contentType", "ContentType", "url=https://example.org")]
    [InlineData("Elsa.FlowSendHttpRequest", "content", "Content", "url=https://example.org")]
    [InlineData("Elsa.WriteHttpResponse", "content", "Body", "")]
    [InlineData("Elsa.WriteHttpResponse", "contentType", "ContentType", "")]
    [InlineData("Elsa.PublishEvent", "correlationId", "CorrelationId", "eventName=order-placed")]
    [InlineData("Elsa.PublishEvent", "payload", "Payload", "eventName=order-placed")]
    [InlineData("Elsa.DispatchWorkflow", "correlationId", "CorrelationId", "workflowDefinitionId=child")]
    [InlineData("Elsa.DispatchWorkflow", "input", "Inputs", "workflowDefinitionId=child")]
    [InlineData("Elsa.ExecuteWorkflow", "input", "Inputs", "workflowDefinitionId=child")]
    public async Task A_null_that_Elsa_3_ran_as_null_is_a_typed_null(string type, string member, string elsa4Key, string others)
    {
        var node = await ConvertAsync(type, member, Null, others);

        var input = Input(node, elsa4Key);
        Assert.Null(input.Value.Value);
        Assert.Equal("Literal", input.Value.ExpressionType);
    }

    [Theory]
    [InlineData("Elsa.If", "condition", "Condition", "")]
    [InlineData("Elsa.While", "condition", "Condition", "")]
    [InlineData("Elsa.FlowDecision", "condition", "Condition", "")]
    [InlineData("Elsa.For", "start", "Start", "")]
    [InlineData("Elsa.For", "end", "End", "")]
    [InlineData("Elsa.HttpEndpoint", "authorize", "Authorize", "path=/orders")]
    [InlineData("Elsa.SendHttpRequest", "method", "Method", "url=https://example.org")]
    [InlineData("Elsa.WriteHttpResponse", "statusCode", "StatusCode", "")]
    [InlineData("Elsa.PublishEvent", "isLocalEvent", "IsLocalEvent", "eventName=order-placed")]
    [InlineData("Elsa.DispatchWorkflow", "waitForCompletion", "WaitForCompletion", "workflowDefinitionId=child")]
    public async Task A_null_that_Elsa_3_ran_as_Elsa_4s_default_gets_no_binding(string type, string member, string elsa4Key, string others)
    {
        var node = await ConvertAsync(type, member, Null, others);

        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == elsa4Key);
    }

    [Theory]
    [InlineData("Elsa.For", "outerBoundInclusive", "EndInclusive", "", "true")]
    [InlineData("Elsa.Delay", "timeSpan", "Duration", "", "00:01:00")]
    [InlineData("Elsa.Timer", "interval", "Interval", "+start", "00:01:00")]
    [InlineData("Elsa.WriteLine", "text", "text", "", "")]
    [InlineData("Elsa.Fault", "code", "code", "", "0")]
    [InlineData("Elsa.Fault", "category", "category", "", "General")]
    [InlineData("Elsa.Fault", "faultType", "faultType", "", "System")]
    [InlineData("Elsa.FlowSendHttpRequest", "expectedStatusCodes", "ExpectedStatusCodes", "url=https://example.org", "[200]")]
    [InlineData("Elsa.Correlate", "correlationId", Elsa3IntrinsicContracts.ValueKey, "", "")]
    [InlineData("Elsa.SetName", "value", Elsa3IntrinsicContracts.ValueKey, "", "")]
    public async Task A_null_that_Elsa_3_ran_with_another_value_than_Elsa_4s_default_gets_that_value(
        string type, string member, string elsa4Key, string others, string expected)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Root(type, member, Null, others)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Text(Input(Node(result.Value!.State, "subject"), elsa4Key)));
        // Elsa 3's value is reproduced, so there is nothing to warn about (FR-007).
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Elsa3MigrationDiagnosticSeverity.Warning && diagnostic.Path.EndsWith($".{member}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Elsa.For", "step", "")]
    [InlineData("Elsa.ForEach", "items", "")]
    [InlineData("Elsa.SendHttpRequest", "requestHeaders", "url=https://example.org")]
    [InlineData("Elsa.FlowSendHttpRequest", "requestHeaders", "url=https://example.org")]
    [InlineData("Elsa.WriteHttpResponse", "responseHeaders", "")]
    [InlineData("Elsa.SendHttpRequest", "contentType", "url=https://example.org;content=hello")]
    [InlineData("Elsa.Cron", "cronExpression", "+start")]
    [InlineData("Elsa.RunJavaScript", "script", "")]
    [InlineData("Elsa.HttpEndpoint", "path", "")]
    [InlineData("Elsa.SendHttpRequest", "url", "")]
    [InlineData("Elsa.Event", "eventName", "")]
    [InlineData("Elsa.PublishEvent", "eventName", "")]
    [InlineData("Elsa.DispatchWorkflow", "workflowDefinitionId", "")]
    [InlineData("Elsa.ExecuteWorkflow", "workflowDefinitionId", "")]
    [InlineData("Elsa.SetOutput", "outputName", "outputValue=42")]
    [InlineData("Elsa.SetOutput", "outputValue", "outputName=Answer")]
    [InlineData("Elsa.SetVariable", "value", "")]
    public async Task A_null_that_Elsa_4_cannot_reproduce_is_refused_once_naming_the_member(string type, string member, string others)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Root(type, member, Null, others)));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.NullUnconvertible);
        Assert.EndsWith($".{member}", refusal.Path, StringComparison.Ordinal);
        Assert.Contains($"'{member}'", refusal.Message, StringComparison.Ordinal);
        // The refusal fails the node, so the required-input check does not report the same member again.
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.RequiredInputUnbound && diagnostic.Path.EndsWith($".{member}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_null_output_is_captured_by_nothing()
    {
        var node = await ConvertAsync("Elsa.RunJavaScript", "result", Null, "script=return 1;");

        Assert.Empty(node.Outputs);
    }

    [Fact]
    public async Task A_null_member_of_an_activity_resolved_by_name_gets_no_binding()
    {
        // A verbatim Elsa 4 catalog key binds members by name; Elsa 4's own default applies, as a reusable's did in Elsa 3.
        var node = await ConvertAsync("Elsa.Activities.Primitives.Activities.Fault", "message", Null, "");

        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == "message");
    }

    [Fact]
    public async Task A_deliberate_null_converts_as_a_JSON_null_does()
    {
        var script = Input(await ConvertAsync("Elsa.RunJavaScript", "possibleOutcomes", LiteralNull, "script=return 1;"), "possibleOutcomes");
        var delay = Input(await ConvertAsync("Elsa.Delay", "timeSpan", LiteralNull, ""), "Duration");

        Assert.Null(script.Value.Value);
        Assert.Equal("Literal", script.Value.ExpressionType);
        Assert.Equal("00:01:00", Text(delay));
    }

    [Fact]
    public async Task A_null_FlowSendHttpRequest_status_list_keeps_Elsa_3s_200_and_unmatched_connections()
    {
        var chart = Flowchart("root",
            Activity("call", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org")), ("expectedStatusCodes", Null)),
            Write("ok"),
            Write("other"));
        chart.Connections = [Connect("call", "200", "ok"), Connect("call", "Unmatched status code", "other")];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var connections = Connections(result.Value!.State.RootActivity!);
        Assert.Contains("call:200->ok", connections);
        Assert.Contains("call:Unmatched status code->other", connections);
    }

    private static async Task<Elsa.Workflows.Design.Core.Models.ActivityNode> ConvertAsync(string type, string member, JsonElement value, string others)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Root(type, member, value, others)));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return Node(result.Value!.State, "subject");
    }

    /// <summary>
    /// A sequence holding the activity under test, <c>subject</c>, with <paramref name="member"/> set to
    /// <paramref name="value"/>. <paramref name="others"/> lists the other members as <c>name=literal</c>, separated by
    /// <c>;</c>; <c>+start</c> marks a start trigger.
    /// </summary>
    private static Elsa3Activity Root(string type, string member, JsonElement value, string others)
    {
        var parts = others.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var properties = parts
            .Where(part => part != "+start")
            .Select(part => part.Split('=', 2))
            .Select(pair => (pair[0], Literal(pair[1])))
            .Append((member, value))
            .ToArray();
        var subject = Activity("subject", type, properties);
        if (parts.Contains("+start"))
            subject.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        return Sequence("root", subject);
    }
}
