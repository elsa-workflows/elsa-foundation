using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Spec 1193 slice C: the server's preparation and the Studio applier implement one contract. Both read the same cases
/// (<c>workflowBatchConformance.json</c> beside the Studio Workflows tests): an accepted case must freeze exactly the
/// recorded batch, which the Studio Vitest applies; a refused case must be refused here, and its laxer frozen form is
/// refused by the applier there. A drift on either side fails one of the two suites.
/// </summary>
public sealed class WorkflowBatchConformanceTests
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var item in Fixture()["cases"]!.AsArray()) data.Add(item!["name"]!.GetValue<string>());
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_server_freezes_or_refuses_each_shared_case_as_recorded(string name)
    {
        var fixture = Fixture();
        var item = fixture["cases"]!.AsArray().Single(candidate => candidate!["name"]!.GetValue<string>() == name)!;
        var catalog = new FixtureActivityCatalog { Entries = CatalogEntries(fixture["catalog"]!.AsArray()) };
        var tool = new WorkflowApplyOperationBatchTool(new AllowAll(), [new ManageOwner()], [catalog]);
        var snapshot = Snapshot(fixture["snapshot"]!);
        var invocation = new AgentToolInvocation("call-1", WorkflowApplyOperationBatchTool.ToolName, "session-1", "actor-1",
            new Dictionary<string, object?> { ["operations"] = JsonSerializer.SerializeToElement(item["operations"]) }, [snapshot])
        { TenantId = "tenant-1", TurnId = "turn-1", Client = snapshot.Client };

        var result = await tool.PrepareAsync(invocation);

        if (item["accepted"]!.GetValue<bool>())
        {
            Assert.True(result.Succeeded, result.Error?.Message);
            var frozen = JsonNode.Parse(result.Value!.Arguments.GetRawText());
            Assert.True(JsonNode.DeepEquals(item["frozen"], frozen), $"The server froze {frozen!.ToJsonString()}");
        }
        else
        {
            Assert.False(result.Succeeded);
            Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, result.Error?.Code);
            Assert.Contains(item["serverReason"]!.GetValue<string>(), result.Error!.Message, StringComparison.Ordinal);
        }
    }

    private static JsonObject Fixture([CallerFilePath] string source = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "..", ".."));
        var path = Path.Combine(root, "Studio", "src", "essentials", "Elsa.Studio.Workflows", "Client", "src", "__tests__", "workflowBatchConformance.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static IReadOnlyList<ActivityAuthoringCatalogEntry> CatalogEntries(JsonArray items) => items.Select(item => new ActivityAuthoringCatalogEntry(
        item!["activityVersionId"]!.GetValue<string>(), item["activityTypeKey"]!.GetValue<string>(), item["version"]!.GetValue<string>(),
        item["displayName"]!.GetValue<string>(), item["category"]?.GetValue<string>(), null, item["available"]?.GetValue<bool>() ?? true, null,
        item["inputs"]!.AsArray().Select(input => new ActivityAuthoringCatalogInput(input!["referenceKey"]!.GetValue<string>(), input["name"]!.GetValue<string>(),
            input["typeName"]!.GetValue<string>(), null, false, input["isCredential"]?.GetValue<bool>() ?? false, input["isCredential"]?.GetValue<bool>() ?? false)).ToArray())).ToArray();

    private static AgentContextAttachment Snapshot(JsonNode content) => new("ctx-1", "workflow.definition", "wf-1", "Workflow", "workflow.definition",
        AgentContextSensitivity.Internal, "selection", "Workflow data", JsonSerializer.SerializeToElement(content), new Dictionary<string, string>())
    {
        ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Origin = AgentContextOrigin.ClientSnapshot,
        Target = new(WorkflowAgentSnapshot.ModuleId, "workflow-definition", "wf-1", content["revision"]!.GetValue<string>()), Client = new("client-1", "generation-1")
    };

    private sealed class AllowAll : Elsa.Weaver.Core.Contracts.IAgentToolAuthorizer
    {
        public Task<AgentPolicyDecision> AuthorizeAsync(Elsa.Weaver.Core.Contracts.AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target,
            CancellationToken cancellationToken = default) => Task.FromResult(new AgentPolicyDecision(true, []));
    }

    private sealed class ManageOwner : Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
