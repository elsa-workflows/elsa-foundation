using Elsa.Primitives.Models;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Mapping;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec1232 SC004: independently authored native and Elsa3 documents through real import/publication/runtime.</summary>
public sealed class NullPolicyNativeConversionParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equal_pinned_policies_produce_equal_values_refusals_and_diagnostics(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var typeKey = typeof(RunJavaScript).FullName!;
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
        var entry = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeKey);
        var scriptVersion = Elsa3ImportHost.Required(entry, "definition", "headVersionId");
        var cases = new[]
        {
            new Case("nullable", "String", "return null;", "null", "null"),
            new Case("scalar", "Int32", "return null;", null, "0", "VF-ACT-004", ["VF-NULL-001"]),
            new Case("undefined", "Any", "return undefined;", null, "null", "VF-UNDEF-001"),
            new Case("partial", "Any", "if (false) return 1;", null, "null", "VF-UNDEF-001", ImportBody: "return undefined;", EditConvertedBody: true),
            new Case("deep", "Any", "return {customer:{order:{discount:undefined}}};", "{\"customer\":{\"order\":{}}}", "{\"customer\":{\"order\":{\"discount\":null}}}", OnWarnings: [], OffWarnings: ["VF-UNDEF-002"]),
            new Case("sparse", "Any", "return [1,,undefined];", "[1,null,null]", "[1,null,null]", OnWarnings: ["VF-UNDEF-003"], OffWarnings: ["VF-UNDEF-003"]),
            new Case("nan", "Any", "return NaN;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("infinity", "Any", "return Infinity;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("negative-infinity", "Any", "return -Infinity;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("stringify", "Any", "return JSON.stringify({discount:undefined,n:NaN});", "\"{\\\"n\\\":null}\"", "\"{\\\"n\\\":null}\""),
            new Case("complete-undefined", "Any", "return complete(undefined, []);", null, "null", "VF-UNDEF-001", ImportBody: "return undefined;", EditConvertedBody: true),
            new Case("member-write", "Any", "variables.captured = {}; variables.captured.discount = undefined; return 1;", "{}", "{\"discount\":null}", OffWarnings: ["VF-UNDEF-002"], Setter: true,
                ImportBody: "setVariable('captured', {}); getVariable('captured').discount = undefined; return 1;"),
            new Case("missing-member-write", "String", "const order = {}; variables.captured = order.discount; return 1;", null, "null", "VF-UNDEF-001", Setter: true,
                ImportBody: "const order = {}; setVariable('captured', order.discount); return 1;"),
            new Case("undefined-write", "String", "variables.captured = undefined; return 1;", null, "null", "VF-UNDEF-001", Setter: true),
            new Case("null-write", "Int32", "variables.captured = null; return 1;", null, "0", "VF-ACT-004", ["VF-NULL-001"], Setter: true)
        };
        foreach (var item in cases)
        {
            output.WriteLine($"BEGIN {item.Name} policies={enabled}");
            var native = await Native(host, scriptVersion, item, enabled);
            var imported = await Imported(host, item, enabled);
            Assert.Equal(native.Workflow!.Status, imported.Workflow!.Status);
            var expected = enabled ? item.On : item.Off;
            if (expected is null)
            {
                Assert.Equal(WorkflowExecutionStatus.Faulted, native.Workflow.Status);
                foreach (var run in new[] { native, imported })
                    Assert.Contains(run.Activities, state => state.Fault?.Message.Contains(item.Refusal!, StringComparison.Ordinal) == true);
            }
            else
            {
                Assert.Equal(WorkflowExecutionStatus.Completed, native.Workflow.Status);
                foreach (var run in new[] { native, imported })
                {
                    Assert.Empty(run.Incidents);
                    var value = run.Workflow!.RootVariableFrame!.Values["captured"];
                    using var oracle = JsonDocument.Parse(expected);
                    Assert.Equal(oracle.RootElement.ValueKind == JsonValueKind.Null ? ValuePresence.ExplicitNull : ValuePresence.Present, value.Presence);
                    if (value.Presence == ValuePresence.Present) Assert.True(JsonElement.DeepEquals(oracle.RootElement, value.InlineValue!.Value), run.Describe());
                    var codes = run.Activities.SelectMany(state => state.Warnings ?? []).Select(warning => warning.Code).Distinct().Order(StringComparer.Ordinal);
                    Assert.Equal(((enabled ? item.OnWarnings : item.OffWarnings) ?? []).Distinct().Order(StringComparer.Ordinal), codes);
                }
            }
            Assert.Equal(Diagnostics(native), Diagnostics(imported));
            output.WriteLine($"PASS {item.Name}: {native.Workflow.Status}; diagnostics={string.Join(',', Diagnostics(native))}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_CLR_non_nullable_String_has_no_fabricated_default_in_native_or_converted_execution(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var key = typeof(Elsa.Activities.Primitives.Activities.PublishEvent).FullName!;
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(key)}");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == key);
        var expression = new ExpressionDefinition("JavaScript", "null", new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>(), JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        var submitted = await host.PostAsync("design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = "native non-nullable String", ["state"] = new JsonObject
            {
                ["variables"] = new JsonArray(), ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["strategyOptions"] = new JsonObject { ["nullHandling"] = new JsonObject { ["defaultOnNull"] = enabled, ["undefinedAsNull"] = enabled } },
                ["rootActivity"] = new JsonObject { ["nodeId"] = "subject", ["activityVersionId"] = Elsa3ImportHost.Required(activity, "definition", "headVersionId"),
                    ["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "EventName", ["value"] = new JsonObject { ["expressionType"] = "JavaScript",
                        ["value"] = JsonSerializer.SerializeToNode(expression, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }), ["outputs"] = new JsonArray() }
            }
        });
        var native = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(submitted, "version", "id")));
        var source = JsonNode.Parse(Elsa3ImportHost.Collection("null-members-script"))!.AsArray()[0]!.DeepClone();
        source["id"] = "s2-string-null-v1"; source["definitionId"] = "s2-string-null";
        source["root"] = new JsonObject { ["id"] = "subject", ["nodeId"] = "subject", ["type"] = "Elsa.PublishEvent", ["version"] = 1,
            ["eventName"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = "null" } } };
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle); var selected = Assert.Single(items);
        var applied = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(selected, "sourceVersionId"));
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var converted = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(selected, "workflowVersionId")));
        foreach (var run in new[] { native, converted })
        {
            // Materialization is refused before Started. The incident remains available for operator recovery.
            Assert.NotEqual(WorkflowExecutionStatus.Completed, run.Workflow!.Status);
            var refused = Assert.Single(run.Activities, state => state.Execution.AuthoredActivityId == "subject");
            Assert.Null(refused.StartedAt); Assert.Null(refused.Completion); Assert.Null(refused.InputSnapshot);
            var incident = Assert.Single(run.Incidents, incident => incident.Message.Contains("VF-ACT-004", StringComparison.Ordinal));
            Assert.Contains("EventName", incident.Message);
            Assert.DoesNotContain("non-blank EventName", incident.Message, StringComparison.Ordinal);
        }
        Assert.Equal(Diagnostics(native), Diagnostics(converted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unbound_no_return_and_complete_undefined_produce_no_assignment_in_both_documents(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeof(RunJavaScript).FullName!)}");
        var version = Elsa3ImportHost.Required(Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeof(RunJavaScript).FullName), "definition", "headVersionId");
        foreach (var body in new[] { "const observed = 1;", "return complete(undefined, []);" })
        {
            var item = new Case("unbound-" + (body.StartsWith("const", StringComparison.Ordinal) ? "no-return" : "complete"), "Any", body, "null", "null", Unbound: true, ImportBody: body.Contains("complete", StringComparison.Ordinal) ? "return undefined;" : null, EditConvertedBody: body.Contains("complete", StringComparison.Ordinal));
            var native = await Native(host, version, item, enabled); var imported = await Imported(host, item, enabled);
            foreach (var run in new[] { native, imported })
            {
                Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow!.Status); Assert.Empty(run.Incidents);
                Assert.Equal(ValuePresence.Absent, run.Workflow.RootVariableFrame!.Values["captured"].Presence);
            }
            Assert.Equal(Diagnostics(native), Diagnostics(imported));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumed_body_without_any_return_is_refused_before_execution_in_both_documents(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeof(RunJavaScript).FullName!)}");
        var version = Elsa3ImportHost.Required(Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeof(RunJavaScript).FullName), "definition", "headVersionId");
        var item = new Case("consumed-no-return", "Any", "const observed = 1;", null, "null", "VF-UNDEF-001", ImportBody: "return undefined;", EditConvertedBody: true);
        // API submission may run the same validation before promotion; otherwise publication must refuse it.
        foreach (var converted in new[] { false, true })
        {
            var exception = await Record.ExceptionAsync(async () =>
            {
                var workflowVersion = converted ? await ImportedVersion(host, item, enabled) : await NativeVersion(host, version, item, enabled);
                using var response = await host.Send(HttpMethod.Post, $"publishing/workflows/{workflowVersion}/publish", new JsonObject());
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("VF-UNDEF-001", body);
            });
            if (exception is not null)
            {
                Assert.Contains("VF-UNDEF-001", exception.Message);
                Assert.True(exception.Message.Contains("definitions/submit", StringComparison.Ordinal) || exception.Message.Contains("/apply", StringComparison.Ordinal), exception.Message);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expression_and_statement_completion_input_boundaries_match_for_native_and_converted_workflows(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        foreach (var alias in new[] { "String", "Boolean", "Int32" })
        foreach (var source in new[] { "null", "undefined", "const value = null; value;", "const value = undefined; value;" })
        {
            var typeKey = alias switch { "String" => "Elsa.Activities.Primitives.Activities.WriteLine", "Boolean" => "Elsa.Activities.Primitives.Activities.PublishEvent", _ => "Elsa.Activities.For.Activities.For" };
            var member = alias switch { "String" => "text", "Boolean" => "IsLocalEvent", _ => "Start" };
            var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
            var activity = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeKey);
            var expression = new ExpressionDefinition("JavaScript", source, new TypeReference(alias), new Dictionary<string, ExpressionParameterBinding>(),
                JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
            var inputs = new JsonArray(new JsonObject { ["referenceKey"] = member, ["value"] = new JsonObject { ["expressionType"] = "JavaScript",
                ["value"] = JsonSerializer.SerializeToNode(expression, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } });
            if (alias == "Boolean") inputs.Add(new JsonObject { ["referenceKey"] = "EventName", ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = "s2-binding-no-subscriber" } });
            var node = new JsonObject { ["nodeId"] = "subject", ["activityVersionId"] = Elsa3ImportHost.Required(activity, "definition", "headVersionId"), ["inputs"] = inputs, ["outputs"] = new JsonArray() };
            if (alias == "Int32") node["structure"] = new JsonObject { ["kind"] = "elsa.for.structure", ["schemaVersion"] = "1.0.0", ["payload"] = new JsonObject { ["body"] = null } };
            var submitted = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "native binding " + alias, ["state"] = new JsonObject {
                ["variables"] = new JsonArray(), ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(), ["rootActivity"] = node,
                ["strategyOptions"] = new JsonObject { ["nullHandling"] = new JsonObject { ["defaultOnNull"] = enabled, ["undefinedAsNull"] = enabled } } } });
            var native = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(submitted, "version", "id")));
            var original = JsonNode.Parse(Elsa3ImportHost.Collection("null-members-script"))!.AsArray()[0]!.DeepClone();
            original["id"] = Guid.NewGuid().ToString("N"); original["definitionId"] = Guid.NewGuid().ToString("N"); original["variables"] = new JsonArray();
            var originalNode = new JsonObject { ["id"] = "subject", ["nodeId"] = "subject", ["type"] = alias == "Int32" ? "Elsa.For" : alias == "Boolean" ? "Elsa.PublishEvent" : "Elsa.WriteLine", ["version"] = 1,
                [alias == "String" ? "text" : alias == "Boolean" ? "isLocalEvent" : "start"] = new JsonObject { ["typeName"] = alias, ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = source } } };
            if (alias == "Boolean") originalNode["eventName"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = "s2-binding-no-subscriber" } };
            if (alias == "Int32")
            {
                originalNode["end"] = new JsonObject { ["typeName"] = "Int32", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = 0 } };
                originalNode["outerBoundInclusive"] = new JsonObject { ["typeName"] = "Boolean", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = false } };
            }
            original["root"] = originalNode;
            var handle = await host.UploadCollectionJsonAsync(new JsonArray(original).ToJsonString());
            var (plan, items) = await host.AnalyzeAsync(handle); var selected = Assert.Single(items);
            var applied = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(selected, "sourceVersionId"));
            Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
            var converted = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(selected, "workflowVersionId")));
            var refused = !enabled && (alias != "String" || source.Contains("undefined", StringComparison.Ordinal));
            foreach (var run in new[] { native, converted })
            {
                var subject = Assert.Single(run.Activities, state => state.Execution.AuthoredActivityId == "subject");
                if (refused)
                {
                    var code = source.Contains("undefined", StringComparison.Ordinal) ? "VF-UNDEF-001" : "VF-ACT-004";
                    Assert.Null(subject.StartedAt); Assert.Null(subject.InputSnapshot);
                    Assert.Contains(run.Incidents, incident => incident.Message.Contains(code, StringComparison.Ordinal));
                }
                else
                {
                    Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow!.Status); Assert.Empty(run.Incidents);
                    var value = subject.InputSnapshot!.Values[member];
                    if (alias == "String") Assert.Equal(ValuePresence.ExplicitNull, value.Presence);
                    else if (alias == "Boolean") Assert.False(value.InlineValue!.Value.GetBoolean());
                    else Assert.Equal(0, value.InlineValue!.Value.GetInt32());
                }
            }
            Assert.Equal(Diagnostics(native), Diagnostics(converted));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exported_definition_keeps_its_policies_after_native_import_on_a_converter_free_host(bool enabled)
    {
        await using var source = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var item = new Case("portable-null", "Int32", "return null;", null, "0", "VF-ACT-004", ["VF-NULL-001"]);
        var convertedVersion = await ImportedVersion(source, item, enabled);
        var original = await source.RunAsync(await source.PublishWorkflowAsync(convertedVersion));
        var version = await source.ReadAsync(services => services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>().GetAsync(convertedVersion));
        var exported = JsonSerializer.SerializeToNode(version.State, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var destination = await Elsa3ImportHost.StartAsync(withConverter: false);
        Assert.False(await destination.ReadAsync(services => Task.FromResult(services.GetService<Elsa3.Mapping.Mappings.Elsa3WorkflowDefinitionToState>() is not null)));
        using var importEndpoint = await destination.Send(HttpMethod.Get, "migration/elsa3/reusable-activities/collections");
        Assert.Equal(HttpStatusCode.NotFound, importEndpoint.StatusCode);
        var imported = await destination.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "native import of converted export", ["state"] = exported });
        var published = await destination.PublishWorkflowAsync(Elsa3ImportHost.Required(imported, "version", "id"));
        var replay = await destination.RunAsync(published);
        Assert.Equal(original.Workflow!.Status, replay.Workflow!.Status);
        Assert.Equal(enabled ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted, replay.Workflow.Status);
        var left = original.Workflow.RootVariableFrame!.Values["captured"]; var right = replay.Workflow.RootVariableFrame!.Values["captured"];
        Assert.Equal(left.Presence, right.Presence);
        if (enabled) Assert.Equal(0, right.InlineValue!.Value.GetInt32());
        Assert.Equal(Diagnostics(original), Diagnostics(replay));
    }

    private static string[] Diagnostics(RunState run) => run.Activities.SelectMany(state => state.Warnings ?? [])
        .Select(warning => $"{warning.Code}|{warning.Severity}|{warning.Message}|{string.Join(';', (warning.Details ?? new Dictionary<string,string>()).OrderBy(pair => pair.Key).Select(pair => pair.Key + '=' + pair.Value))}")
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task<RunState> Native(Elsa3ImportHost host, string scriptVersion, Case item, bool enabled) =>
        await host.RunAsync(await host.PublishWorkflowAsync(await NativeVersion(host, scriptVersion, item, enabled)));

    private static async Task<string> NativeVersion(Elsa3ImportHost host, string scriptVersion, Case item, bool enabled)
    {
        var state = JsonNode.Parse("""{"variables":[{"referenceKey":"captured","name":"captured","type":{"alias":"Any","collectionKind":"Single"}}],"inputs":[],"outputs":[],"rootActivity":{"nodeId":"script","inputs":[],"outputs":[]},"strategyOptions":{"nullHandling":{}}}""")!;
        state["variables"]![0]!["type"]!["alias"] = item.Alias == "Any" ? TypeAliasConvention.AnyAlias : item.Alias;
        state["rootActivity"]!["activityVersionId"] = scriptVersion;
        state["rootActivity"]!["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "script", ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = item.Body } });
        if (!item.Setter && !item.Unbound) state["rootActivity"]!["outputs"] = JsonNode.Parse("""[{"referenceKey":"result","value":{"expressionType":"Variable","value":{"referenceKey":"captured","declaringScopeId":"workflow"}}}]""");
        state["strategyOptions"]!["nullHandling"]!["defaultOnNull"] = enabled;
        state["strategyOptions"]!["nullHandling"]!["undefinedAsNull"] = enabled;
        var submitted = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "native parity " + item.Name, ["state"] = state });
        return Elsa3ImportHost.Required(submitted, "version", "id");
    }

    private static async Task<RunState> Imported(Elsa3ImportHost host, Case item, bool enabled) =>
        await host.RunAsync(await host.PublishWorkflowAsync(await ImportedVersion(host, item, enabled)));

    private static async Task<string> ImportedVersion(Elsa3ImportHost host, Case item, bool enabled)
    {
        var source = JsonNode.Parse(Elsa3ImportHost.Collection("null-members-script"))!.AsArray()[0]!.DeepClone();
        var id = "s2-parity-" + item.Name + '-' + enabled;
        source["id"] = id + "-v1"; source["definitionId"] = id; source["name"] = id;
        source["variables"] = new JsonArray(new JsonObject { ["id"] = "captured", ["name"] = "captured", ["typeName"] = item.Alias == "Any" ? "Object" : item.Alias });
        var script = new JsonObject { ["id"] = "script", ["nodeId"] = "script", ["type"] = "Elsa.RunJavaScript", ["version"] = 1,
            ["script"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = item.ImportBody ?? (item.Setter ? item.Body.Replace("variables.captured = undefined", "setVariable('captured', undefined)", StringComparison.Ordinal).Replace("variables.captured = null", "setVariable('captured', null)", StringComparison.Ordinal) : item.Body) } } };
        if (!item.Setter && !item.Unbound) script["result"] = JsonNode.Parse("""{"typeName":"Object","memoryReference":{"id":"captured"}}""");
        source["root"] = script;
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var selected = Assert.Single(items);
        var apply = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(selected, "sourceVersionId"));
        Assert.True(apply.StatusCode == HttpStatusCode.Created, apply.Body.ToJsonString());
        var convertedVersion = Elsa3ImportHost.Required(selected, "workflowVersionId");
        if (!item.EditConvertedBody) return convertedVersion;
        // Elsa3 does not expose Elsa4's complete helper, and its converter deliberately refuses terminal
        // statement shapes (S-3). Exercise edits of a supported conversion through the native definition API;
        // this qualifies policy portability after editing, not direct conversion of those unsupported sources.
        var version = await host.ReadAsync(services => services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>().GetAsync(convertedVersion));
        var exported = JsonSerializer.SerializeToNode(version.State, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var scriptInput = exported["rootActivity"]!["inputs"]!.AsArray().Single(input => (string?)input?["referenceKey"] == "script")!;
        scriptInput["value"]!["value"] = item.Body;
        var edited = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "edited converted " + item.Name, ["state"] = exported });
        var editedVersionId = Elsa3ImportHost.Required(edited, "version", "id");
        var editedVersion = await host.ReadAsync(services => services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>().GetAsync(editedVersionId));
        Assert.Equal(version.State!.StrategyOptions?.NullHandling?.DefaultOnNull, editedVersion.State!.StrategyOptions?.NullHandling?.DefaultOnNull);
        Assert.Equal(version.State.StrategyOptions?.NullHandling?.UndefinedAsNull, editedVersion.State.StrategyOptions?.NullHandling?.UndefinedAsNull);
        return editedVersionId;
    }

    private sealed record Case(string Name, string Alias, string Body, string? Off, string On, string? Refusal = null,
        string[]? OnWarnings = null, string[]? OffWarnings = null, bool Setter = false, bool Unbound = false, string? ImportBody = null, bool EditConvertedBody = false);
}
