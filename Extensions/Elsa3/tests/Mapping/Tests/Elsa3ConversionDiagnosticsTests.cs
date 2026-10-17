using System.Text.Json;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 US2 and SC-003: every unconvertible cause returns its stable code with the node, path and guidance, every
/// documented runtime difference is a warning, diagnostics are complete and ordered, and no exception escapes.
/// </summary>
public sealed class Elsa3ConversionDiagnosticsTests
{
    public static TheoryData<string, string> UnconvertibleCases() => new()
    {
        { "no-counterpart:StartAt", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:StateMachine", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:ParallelForEach", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:RunTask", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:BulkDispatchWorkflows", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:DownloadHttpFile", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "no-counterpart:WriteFileHttpResponse", Elsa3ActivityDiagnosticCodes.NoCounterpart },
        { "unmapped", Elsa3ActivityDiagnosticCodes.UnmappedType },
        { "unknown-member", Elsa3ActivityDiagnosticCodes.UnknownMember },
        { "semantics-changed", Elsa3ActivityDiagnosticCodes.MemberSemanticsChanged },
        // Flowchart constructs mean nothing outside a Flowchart (spec 239 R-2); a Flowchart itself now converts.
        { "graph:FlowFork", Elsa3ActivityDiagnosticCodes.UnsupportedMode },
        { "graph:FlowJoin", Elsa3ActivityDiagnosticCodes.UnsupportedMode },
        { "graph:Start", Elsa3ActivityDiagnosticCodes.UnsupportedMode },
        { "version-unavailable", Elsa3ActivityDiagnosticCodes.VersionUnavailable },
        { "structure-unreadable", Elsa3ActivityDiagnosticCodes.StructureUnreadable },
        { "removed-input-bound", Elsa3ActivityDiagnosticCodes.RemovedInputBound },
        { "unsupported-mode", Elsa3ActivityDiagnosticCodes.UnsupportedMode },
        { "value-outside-contract", Elsa3ActivityDiagnosticCodes.ValueOutsideContract },
        { "output-not-representable", Elsa3ActivityDiagnosticCodes.OutputNotRepresentable },
        { "required-input-unbound", Elsa3ActivityDiagnosticCodes.RequiredInputUnbound },
        { "nesting-too-deep", Elsa3ActivityDiagnosticCodes.NestingTooDeep },
        { "duplicate-input-binding", Elsa3ActivityDiagnosticCodes.DuplicateInputBinding },
    };

    /// <summary>The behaviour warnings plain workflows raise; the two reusable-outcome warnings are spec 236 US4's.</summary>
    public static TheoryData<string> WarningCases() =>
    [
        Elsa3ActivityDiagnosticCodes.FormPostNotParsed,
        Elsa3ActivityDiagnosticCodes.RequestSizeNoOutcome,
        Elsa3ActivityDiagnosticCodes.HttpOutcomeSingle,
        Elsa3ActivityDiagnosticCodes.AuthHeaderUnvalidated,
        Elsa3ActivityDiagnosticCodes.HttpAuthorizationUseConnection,
        Elsa3ActivityDiagnosticCodes.WhilePersistedCondition,
        Elsa3ActivityDiagnosticCodes.ForZeroStepFaults,
        Elsa3ActivityDiagnosticCodes.BreakStopsAtParallel,
        Elsa3ActivityDiagnosticCodes.ForkWaitAnyCancellation,
        Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable,
        Elsa3ActivityDiagnosticCodes.ScriptOutcomeRouting,
    ];

    [Theory]
    [MemberData(nameof(UnconvertibleCases))]
    public async Task Each_unconvertible_cause_returns_its_code_at_the_node(string kind, string code)
    {
        var (catalog, root, nodeId) = Unconvertible(kind);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(nodeId, diagnostic.Metadata["ActivityNodeId"]);
        Assert.Equal("definition-1", diagnostic.Metadata["DefinitionId"]);
        Assert.Equal("1", diagnostic.Metadata["DefinitionVersion"]);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
        Assert.StartsWith("$.root", diagnostic.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, other => other.Code == Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed);
    }

    [Theory]
    [MemberData(nameof(WarningCases))]
    public async Task Each_runtime_difference_converts_with_its_warning(string code)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Warning(code)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.False(string.IsNullOrWhiteSpace(warning.Message));
    }

    [Fact]
    public async Task Several_causes_are_all_reported_in_path_then_code_order()
    {
        var root = Sequence("root",
            Activity("unknown", "Acme.Unknown"),
            Activity("write", "Elsa.WriteLine", ("bogus", Literal("x"))),
            Activity("state", "Elsa.StateMachine"));

        var first = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));
        var second = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.Equal(
            [
                Elsa3ActivityDiagnosticCodes.UnmappedType,
                Elsa3ActivityDiagnosticCodes.UnknownMember,
                Elsa3ActivityDiagnosticCodes.NoCounterpart
            ],
            first.Diagnostics.Select(diagnostic => diagnostic.Code));
        var paths = first.Diagnostics.Select(diagnostic => diagnostic.Path!).ToArray();
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
        Assert.Equal(first.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)), second.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public async Task Removed_inputs_at_their_default_are_removed_without_a_diagnostic()
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint",
            ("path", Literal("/x")),
            ("allowedMimeTypes", Literal("[]", "Object")),
            ("exposeFileTooLargeOutcome", Literal("false", "Boolean")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", endpoint)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Cancellation_still_throws()
    {
        await using var provider = ConversionServices.Create(RealActivityCatalog.Create());
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(
            new Elsa3ConversionRequest(ConversionServices.Definition(Sequence("root"))), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Malformed_ports_and_structure_members_return_diagnostics_never_an_exception()
    {
        var @if = Activity("if", "Elsa.If", ("then", JsonSerializer.SerializeToElement("not an activity")));
        var @switch = Activity("switch", "Elsa.Switch", ("cases", JsonSerializer.SerializeToElement(new { label = "not an array" })));
        var set = Activity("set", "Elsa.SetVariable", ("value", Literal("x")), ("variable", JsonSerializer.SerializeToElement(42)));
        var fork = Activity("fork", "Elsa.Fork", ("joinMode", JsonSerializer.SerializeToElement(new { mode = "?" })));
        var complete = Activity("complete", "Elsa.Complete", ("outcomes", Literal("[\"a\",")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @if, @switch, set, fork, complete)));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed);
    }

    private static (RealActivityCatalog Catalog, Elsa3Activity Root, string NodeId) Unconvertible(string kind)
    {
        var catalog = RealActivityCatalog.Create();
        if (kind.StartsWith("no-counterpart:", StringComparison.Ordinal) || kind.StartsWith("graph:", StringComparison.Ordinal))
        {
            var type = kind[(kind.IndexOf(':') + 1)..];
            return (catalog, Sequence("root", Activity("node", $"Elsa.{type}")), "node");
        }

        Elsa3Activity node;
        switch (kind)
        {
            case "unmapped":
                node = Activity("node", "Acme.Unknown");
                break;
            case "unknown-member":
                node = Activity("node", "Elsa.WriteLine", ("text", Literal("x")), ("bogus", Literal("y")));
                break;
            case "semantics-changed":
                node = Activity("node", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org")), ("parsedContent", Captured("body")));
                break;
            case "version-unavailable":
                catalog.WithoutVersions("Elsa.Activities.Primitives.Activities.WriteLine");
                node = Activity("node", "Elsa.WriteLine", ("text", Literal("x")));
                break;
            case "structure-unreadable":
                catalog.WithUnreadableStructure("Elsa.Activities.If.Activities.If");
                node = Activity("node", "Elsa.If", ("condition", Literal("true", "Boolean")));
                break;
            case "removed-input-bound":
                node = Activity("node", "Elsa.HttpEndpoint", ("path", Literal("/x")), ("exposeFileTooLargeOutcome", Literal("true", "Boolean")));
                node.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
                break;
            case "unsupported-mode":
                node = Activity("node", "Elsa.Switch", ("mode", Expression("JavaScript", "'MatchAny'")));
                break;
            case "value-outside-contract":
                node = Activity("node", "Elsa.HttpEndpoint", ("path", Expression("JavaScript", "'/x'")));
                node.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
                break;
            case "output-not-representable":
                node = Activity("node", "Elsa.Event", ("eventName", Literal("e")), ("result", Captured("payload")));
                break;
            case "required-input-unbound":
                node = Activity("node", "Elsa.WriteLine");
                break;
            case "duplicate-input-binding":
                node = Activity("node", "Elsa.WriteLine", ("text", Literal("a")), ("Text", Literal("b")));
                break;
            case "nesting-too-deep":
                node = Activity("node", "Elsa.WriteLine", ("text", Literal("deep")));
                var root = Enumerable.Range(0, Elsa3ActivityNormalizer.MaxSequenceNestingDepth + 1)
                    .Aggregate(node, (child, level) => Sequence($"level-{level}", child));
                return (catalog, root, "node");
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return (catalog, Sequence("root", node), "node");
    }

    private static Elsa3Activity Warning(string code)
    {
        switch (code)
        {
            case Elsa3ActivityDiagnosticCodes.FormPostNotParsed:
            case Elsa3ActivityDiagnosticCodes.RequestSizeNoOutcome:
                var endpoint = code == Elsa3ActivityDiagnosticCodes.FormPostNotParsed
                    ? Activity("node", "Elsa.HttpEndpoint", ("path", Literal("/x")), ("parsedContent", Captured("body")))
                    : Activity("node", "Elsa.HttpEndpoint", ("path", Literal("/x")), ("requestSizeLimit", Literal("1024", "Int64")));
                endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
                return Sequence("root", endpoint);
            case Elsa3ActivityDiagnosticCodes.HttpOutcomeSingle:
                return Sequence("root", Activity("node", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org")), ("disableAuthorizationHeaderValidation", Literal("true", "Boolean"))));
            case Elsa3ActivityDiagnosticCodes.AuthHeaderUnvalidated:
                return Sequence("root", Activity("node", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org"))));
            case Elsa3ActivityDiagnosticCodes.HttpAuthorizationUseConnection:
                return Sequence("root", Activity("node", "Elsa.SendHttpRequest", ("url", Literal("https://example.org")), ("authorization", Literal("Bearer x"))));
            case Elsa3ActivityDiagnosticCodes.WhilePersistedCondition:
                return Sequence("root", Activity("node", "Elsa.While", ("condition", Literal("false", "Boolean"))));
            case Elsa3ActivityDiagnosticCodes.ForZeroStepFaults:
                return Sequence("root", Activity("node", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("1", "Int32")), ("step", Literal("0", "Int32"))));
            case Elsa3ActivityDiagnosticCodes.BreakStopsAtParallel:
                var parallel = Activity("node", "Elsa.Parallel");
                parallel.Activities = [Activity("break", "Elsa.Break")];
                return Sequence("root", parallel);
            case Elsa3ActivityDiagnosticCodes.ForkWaitAnyCancellation:
                var fork = Activity("node", "Elsa.Fork", ("joinMode", JsonSerializer.SerializeToElement("WaitAny")));
                fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[] { Port(Activity("a", "Elsa.WriteLine", ("text", Literal("a")))) });
                return Sequence("root", fork);
            case Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable:
                return Sequence("root", Activity("node", "Elsa.ExecuteWorkflow", ("workflowDefinitionId", Literal("child"))));
            case Elsa3ActivityDiagnosticCodes.ScriptOutcomeRouting:
                return Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("'a'")), ("possibleOutcomes", Literal("[\"a\"]", "Object"))));
            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, null);
        }
    }
}
