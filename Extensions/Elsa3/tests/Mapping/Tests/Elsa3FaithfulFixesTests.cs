using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 248 (#3051): members the JTest measurement showed the converter refused although Elsa 4 behaves the same as
/// Elsa 3. Each row converts against the real Elsa 4 activity catalog; the nearby refusals stay.
/// </summary>
public sealed class Elsa3FaithfulFixesTests
{
    // ── Row 1: WriteLine.text unset is an empty line (R2) ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_WriteLine_without_text_writes_an_empty_line()
    {
        var state = await ConvertAsync(Sequence("root", Activity("write", "Elsa.WriteLine")));

        Assert.Equal(string.Empty, Text(Input(Node(state, "write"), "text")));
    }

    [Fact]
    public async Task A_WriteLine_with_an_empty_literal_writes_an_empty_line()
    {
        var state = await ConvertAsync(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("")))));

        Assert.Equal(string.Empty, Text(Input(Node(state, "write"), "text")));
    }

    [Fact]
    public async Task Another_required_input_with_no_Elsa3_source_is_still_refused()
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("publish", "Elsa.PublishEvent"))));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.RequiredInputUnbound);
    }

    // ── Row 2: canStartWorkflow on a non-trigger only picks the flowchart start ──────────────────────────────────

    [Fact]
    public async Task A_marked_non_trigger_is_the_flowchart_start_and_keeps_no_flag()
    {
        var chart = Flowchart("root", Write("first"), Marked(Write("start")));
        chart.Connections = [Connect("start", "Done", "first")];

        var state = await ConvertAsync(chart);

        Assert.Equal("start", StartNodeId(state.RootActivity!));
        Assert.DoesNotContain(Node(state, "start").Inputs, input => input.ReferenceKey == "CanStartWorkflow");
    }

    [Fact]
    public async Task A_marked_non_trigger_outside_a_flowchart_converts()
    {
        var state = await ConvertAsync(Sequence("root", Marked(Write("write"))));

        Assert.Equal("write", Text(Input(Node(state, "write"), "text")));
    }

    [Fact]
    public async Task A_marked_non_trigger_that_is_led_into_is_not_a_misplaced_start_trigger()
    {
        // Elsa 3 starts at the first marked activity; the second never fires, so its flag has no effect.
        var chart = Flowchart("root", Marked(Write("a")), Marked(Write("b")));
        chart.Connections = [Connect("a", "Done", "b")];

        var state = await ConvertAsync(chart);

        Assert.Equal("a", StartNodeId(state.RootActivity!));
    }

    [Fact]
    public async Task A_start_only_trigger_used_mid_flow_is_still_refused()
    {
        var timer = Activity("timer", "Elsa.Timer", ("interval", Literal("00:00:05", "TimeSpan")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", timer)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.NoCounterpart);
    }

    // ── Row 3: ObjectDictionary is JSON ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Elsa.DispatchWorkflow")]
    [InlineData("Elsa.ExecuteWorkflow")]
    public async Task Workflow_input_declared_ObjectDictionary_converts(string type)
    {
        var node = Activity("child", type,
            ("workflowDefinitionId", Literal("child-definition")),
            ("input", Expression("JavaScript", "return { Name: 'x' };", "ObjectDictionary")));

        var state = await ConvertAsync(Sequence("root", node));

        Assert.Contains("Name: 'x'", Source(Input(Node(state, "child"), "Inputs")));
    }

    [Fact]
    public async Task A_json_only_member_declared_as_a_number_is_still_refused()
    {
        var node = Activity("child", "Elsa.DispatchWorkflow",
            ("workflowDefinitionId", Literal("child-definition")),
            ("input", Expression("JavaScript", "1", "Int32")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", node)));

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract);
    }

    // ── Row 4: VF-IMP-008 judges the normalized Elsa 4 contract ──────────────────────────────────────────────────

    [Fact]
    public async Task An_Elsa3_status_code_name_and_header_map_convert()
    {
        var respond = Activity("respond", "Elsa.WriteHttpResponse",
            ("statusCode", Literal("Accepted", "System.Net.HttpStatusCode, System.Net.Primitives")),
            ("content", Literal("ok")),
            ("responseHeaders", Literal("{}", "HttpHeaders")));

        var state = await ConvertAsync(Sequence("root", respond));

        var node = Node(state, "respond");
        Assert.Equal("202", Text(Input(node, "StatusCode")));
        Assert.Equal("{}", Text(Input(node, "Headers")));
    }

    [Fact]
    public async Task Supported_methods_declared_as_a_string_list_convert()
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint",
            ("path", Literal("/orders")),
            ("supportedMethods", Literal("[\"POST\"]", "List<String>")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };

        var state = await ConvertAsync(Sequence("root", endpoint));

        Assert.Equal("[\"POST\"]", Text(Input(Node(state, "endpoint"), "SupportedMethods")));
    }

    [Fact]
    public async Task A_genuinely_incompatible_declared_type_is_still_refused()
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint",
            ("path", Literal("/orders")),
            ("supportedMethods", Expression("JavaScript", "true", "Boolean")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", endpoint)));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.IncompatibleValue);
    }

    private static Elsa3Activity Marked(Elsa3Activity activity)
    {
        activity.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        return activity;
    }

    private static async Task<Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState> ConvertAsync(Elsa3Activity root)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return result.Value!.State;
    }
}
