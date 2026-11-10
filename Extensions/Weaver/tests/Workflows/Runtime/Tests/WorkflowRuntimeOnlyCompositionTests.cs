using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Weaver.Workflows.Runtime.Tests.WorkflowRuntimeAgentTestHost;

namespace Elsa.Weaver.Workflows.Runtime.Tests;

// Spec 1193 SC-009 (FR-027): a Runtime-only host composes the Runtime-side workflow package without any Workflows Design
// assembly, serves the Executable and Run journeys, and reports every Design journey (a draft, a version) as explicitly unavailable.
public sealed class WorkflowRuntimeOnlyCompositionTests
{
    [Fact]
    public async Task A_runtime_only_host_serves_the_Executable_and_Run_journeys_with_no_Design_assembly_loaded()
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var collected = await host.Services.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new(session.Id, "workflow-executable", new Dictionary<string, string>()) { CallerAttachments = [Reference()] });

        Assert.True(collected.Succeeded, collected.Error?.Message);
        Assert.Single(collected.Value!.Attachments, item => item.ContentType == "workflows.executable");
        var prepared = await host.PrepareAsync(session, collected.Value.Attachments);
        Assert.True(prepared.Succeeded);
        // The Run journey (slice G) composes in the same Runtime-only host.
        var runs = await host.Services.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new(session.Id, "workflow-run", new Dictionary<string, string>()) { CallerAttachments = [RunReference()] });
        Assert.True(runs.Succeeded, runs.Error?.Message);
        Assert.True((await host.PrepareRunAsync(session, runs.Value!.Attachments)).Succeeded);

        // AppDomain-wide, after composing and exercising the host: no Workflows Design assembly is loaded or referenced.
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? "").ToArray();
        Assert.Contains("Elsa.Weaver.Workflows.Runtime", loaded);
        Assert.DoesNotContain(loaded, name => name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal));
        Assert.DoesNotContain(loaded, name => name == "Elsa.Weaver.Workflows");
        Assert.DoesNotContain(typeof(WeaverWorkflowsRuntimeFeature).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal) || reference.Name == "Elsa.Weaver.Workflows");
    }

    [Theory]
    [InlineData("workflow.definition", "workflow-definition")]
    [InlineData("workflows.version", "workflow-version")]
    [InlineData("workflows.library", "workflow-library")]
    public async Task A_runtime_only_host_reports_Design_journeys_as_explicitly_unavailable(string kind, string resourceType)
    {
        using var host = new WorkflowRuntimeAgentTestHost();
        var session = await host.SessionAsync();
        var design = new AgentContextAttachment($"{kind}:1", "workflow", "resource-1", "Design context", kind, AgentContextSensitivity.Internal, "selection",
            "Design context", JsonSerializer.SerializeToElement(new { definitionId = "definition-1", versionId = "version-1" }), new Dictionary<string, string>())
        {
            ModuleId = ModuleId, SchemaVersion = "1", Target = new(ModuleId, resourceType, "resource-1", "resource-1"), Origin = AgentContextOrigin.ClientSnapshot
        };

        var collected = await host.Services.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new(session.Id, "workflow", new Dictionary<string, string>()) { CallerAttachments = [design] });

        Assert.False(collected.Succeeded);
        var refusal = Assert.Single(collected.Value!.Diagnostics);
        Assert.Equal(("weaver.context.kind_unsupported", true), (refusal.Code, refusal.Required));
        Assert.Equal("No installed module admits the supplied context kind and owner.", refusal.Message);
        Assert.Empty(collected.Value.Attachments);
        // And no Design tool is installed to reach the draft or a version another way.
        var tools = (await host.Services.GetRequiredService<IAgentToolRegistry>().ListAsync()).Select(tool => tool.Descriptor.Name).ToArray();
        Assert.Equal(["workflow_read_executable", "workflow_read_run"], tools.Where(name => name.StartsWith("workflow_", StringComparison.Ordinal)).Order());
    }

    [Fact]
    public async Task The_composed_runtime_registrations_equal_the_shared_registrations_the_Studio_contributions_reference()
    {
        // SC-007: the Weaver Workflows Vitest holds every prompt and tool contract to the same file's runtime section.
        var fixture = Registrations()["runtime"]!.AsObject();
        using var host = new WorkflowRuntimeAgentTestHost();
        Assert.Equal("Elsa.Weaver.Workflows.Runtime", fixture["package"]!.GetValue<string>());
        Assert.Equal(fixture["package"]!.GetValue<string>(), typeof(WeaverWorkflowsRuntimeFeature).Assembly.GetName().Name);
        Assert.Equal(fixture["contextSourceId"]!.GetValue<string>(), host.Source.Descriptor.Id);
        Assert.Equal(ModuleId, host.Source.Descriptor.ModuleId);
        Assert.Equal(Strings(fixture["contextKinds"]!), host.Source.Descriptor.ContextKinds);
        // The Run context source (spec 1193 slice G) beside it, under the same module identity.
        Assert.Equal(fixture["runSource"]!["id"]!.GetValue<string>(), host.RunSource.Descriptor.Id);
        Assert.Equal(ModuleId, host.RunSource.Descriptor.ModuleId);
        Assert.Equal(Strings(fixture["runSource"]!["contextKinds"]!), host.RunSource.Descriptor.ContextKinds);
        var capabilities = await host.Services.GetServices<IAgentCapabilityProvider>().OfType<WorkflowRuntimeAgentCapabilityProvider>().Single().GetCapabilitiesAsync();
        Assert.Equal(Strings(fixture["capabilities"]!), capabilities.Select(item => item.Id));
        var tools = (await host.Services.GetRequiredService<IAgentToolRegistry>().ListAsync()).Where(tool => tool.Descriptor.ModuleId == ModuleId)
            .Select(tool => tool.Descriptor).ToArray();
        Assert.Equal(fixture["tools"]!.AsArray().Select(item => (item!["name"]!.GetValue<string>(), item["resultType"]!.GetValue<string>(),
                item["rendererId"]?.GetValue<string>(), item["executionLocation"]!.GetValue<string>(), item["disclosureSourceId"]!.GetValue<string>())),
            tools.Select(tool => (tool.Name, tool.ResultType!, tool.ResultRendererId, JsonNamingPolicy.CamelCase.ConvertName(tool.ExecutionLocation.ToString()),
                tool.DisclosureSourceId!)));
        // The texts name only this package's registered tool.
        var guidance = WorkflowExecutableContext.Guidance().Summary + " " + WorkflowRunContext.Guidance().Summary + " " + string.Join(" ", tools.Select(tool => tool.Description))
            + " " + string.Join(" ", capabilities.Select(item => item.Description));
        foreach (var token in System.Text.RegularExpressions.Regex.Matches(guidance, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b").Select(match => match.Value))
            Assert.Contains(token, tools.Select(tool => tool.Name));
    }

    private static string[] Strings(JsonNode node) => node.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static JsonObject Registrations([CallerFilePath] string source = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "Extensions", "Weaver", "studio", "Elsa.Studio.Weaver.Workflows", "Client", "src", "__tests__", "workflowAgentRegistrations.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
