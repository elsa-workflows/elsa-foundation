using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Spec 1193 slice C (stories 2 and 3; FR-007, FR-012, FR-013, FR-015, FR-017, SC-004): the reviewed Operation Batch is
/// strict per operation, validated against the admitted working draft and the owning catalog, classified per operation,
/// always reviewed, and refused or clarified whole with nothing changed.
/// </summary>
public sealed class WorkflowOperationBatchTests
{
    private const string FlowSlot = "elsa.flowchart.structure:activities";
    private const string SequenceSlot = "elsa.sequence.structure:activities";

    [Fact]
    public async Task A_batch_of_many_kinds_in_a_nested_scope_is_frozen_with_resolved_targets_and_reviewed()
    {
        var result = await PrepareAsync(
            new { kind = "add", @ref = "greet", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow", "seq" }, slot = SequenceSlot, position = new { x = 40, y = 80 } },
            new { kind = "set-property", activity = new { @ref = "greet" }, property = "Text", value = new { literal = "Hello" } },
            new { kind = "update", activity = new { id = "write-1" }, fields = new { displayName = "Say hello", description = "Greets the user." } },
            new { kind = "position", activity = new { name = "Inner sequence" }, x = 300, y = 120 },
            new { kind = "connect", source = new { id = "write-1" }, target = new { id = "legacy" }, sourcePort = "Done" },
            new { kind = "disconnect", source = new { id = "write-1" }, target = new { id = "write-2" } },
            new { kind = "set-property", activity = new { id = "write-1" }, property = "ApiKey", value = new { secretReference = new { name = "smtp-key" } } });

        Assert.True(result.Succeeded, result.Error?.Message);
        var preparation = result.Value!;
        Assert.True(preparation.RequiresApproval);
        Assert.Equal(Snapshot().Target, preparation.Target);
        Assert.Equal(Snapshot().Client, preparation.Client);
        // The batch takes its highest operation risk: the disconnection is destructive.
        Assert.Equal(AgentRisk.Destructive, preparation.Risk);
        var operations = preparation.Arguments.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(7, operations.Length);
        Assert.Equal("greet", operations[0].GetProperty("nodeId").GetString());
        Assert.Equal("version-write-line", operations[0].GetProperty("activityVersionId").GetString());
        Assert.Equal(["flow", "seq"], operations[0].GetProperty("scopePath").EnumerateArray().Select(item => item.GetString()));
        // A property is frozen to its declared reference key; a name reference to the one node it names.
        Assert.Equal("Text", operations[1].GetProperty("property").GetString());
        Assert.Equal("seq", operations[3].GetProperty("nodeId").GetString());
        Assert.Equal("smtp-key", operations[6].GetProperty("value").GetProperty("secretReference").GetProperty("name").GetString());
        var labels = preparation.Preview.Data.GetProperty("operations").EnumerateArray().Select(item => item.GetProperty("risk").GetString()!).ToArray();
        Assert.Equal(["reviewRequired", "reviewRequired", "reviewRequired", "reviewRequired", "reviewRequired", "destructive", "reviewRequired"], labels);
        Assert.Equal(WorkflowOperationBatch.ResultType, preparation.Preview.TypeId);
        Assert.False(preparation.Preview.Data.GetProperty("highComplexity").GetBoolean());
        Assert.Equal(["testRunResult", "publishedArtifactAssociation"], preparation.Preview.Data.GetProperty("clears").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Ordinary_edits_are_review_required_and_still_reviewed()
    {
        var result = await PrepareAsync(new { kind = "position", activity = new { id = "write-1" }, x = 10, y = 20 });

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(AgentRisk.ReviewRequired, result.Value!.Risk);
        Assert.True(result.Value.RequiresApproval);
        Assert.Equal(AgentExecutionLocation.ClientWorkingState, WorkflowApplyOperationBatchTool.Contract.ExecutionLocation);
        Assert.Equal("workflows/design:manage", Assert.Single(WorkflowApplyOperationBatchTool.Contract.RequiredPermissions));
    }

    [Theory]
    [InlineData("""{"kind":"add","ref":"a","activityTypeKey":"Acme.version-write-line","version":"1.0.0","scopePath":["flow"],"slot":"elsa.flowchart.structure:activities","displayName":"x"}""", "Unknown field")]
    [InlineData("""{"kind":"add","ref":"a","activityTypeKey":"Acme.version-write-line","version":"1.0.0","scopePath":["flow"]}""", "'slot'")]
    [InlineData("""{"kind":"add","ref":"a","activityTypeKey":"Acme.version-write-line","version":"1.0.0","slot":"elsa.flowchart.structure:activities"}""", "'scopePath'")]
    [InlineData("""{"kind":"add","ref":"Not Valid","activityTypeKey":"Acme.version-write-line","version":"1.0.0","scopePath":["flow"],"slot":"elsa.flowchart.structure:activities"}""", "'ref' must match")]
    [InlineData("""{"kind":"set-root","activity":{"ref":"a"},"replace":true}""", "Unknown field")]
    [InlineData("""{"kind":"position","activity":{"id":"write-1"},"x":1,"y":2,"z":3}""", "Unknown field")]
    [InlineData("""{"kind":"position","activity":{"id":"write-1"},"x":1}""", "'y'")]
    [InlineData("""{"kind":"set-property","activity":{"id":"write-1"},"value":{"literal":"x"}}""", "'property'")]
    [InlineData("""{"kind":"set-property","activity":{"id":"write-1"},"property":"Text","value":{"literal":"x"},"typeName":"String"}""", "Unknown field")]
    [InlineData("""{"kind":"set-property","activity":{"id":"write-1"},"property":"Text","value":{"literal":{"nested":true}}}""", "'literal'")]
    [InlineData("""{"kind":"update","activity":{"id":"write-1"},"fields":{"activityVersionId":"other"}}""", "Unknown field")]
    [InlineData("""{"kind":"update","activity":{"id":"write-1"},"fields":{"name":"x"}}""", "Unknown field")]
    [InlineData("""{"kind":"update","activity":{"id":"write-1"},"fields":{}}""", "at least one")]
    [InlineData("""{"kind":"remove","activity":{"id":"write-1"},"cascade":true}""", "Unknown field")]
    [InlineData("""{"kind":"connect","source":{"id":"write-1"},"target":{"id":"write-2"},"vertices":[]}""", "Unknown field")]
    [InlineData("""{"kind":"disconnect","source":{"id":"write-1"},"target":{"id":"write-2"},"targetPort":"In"}""", "Unknown field")]
    [InlineData("""{"kind":"position","activity":{"id":"write-1","name":"Write line"},"x":1,"y":2}""", "exactly one of")]
    [InlineData("""{"kind":"rewrite-type","activity":{"id":"write-1"}}""", "Unknown operation kind")]
    public async Task Each_operation_kind_refuses_anything_outside_its_strict_schema(string operation, string expected)
    {
        var result = await PrepareAsync(JsonDocument.Parse($$"""{"operations":[{"kind":"position","activity":{"id":"write-2"},"x":1,"y":1},{{operation}}]}""").RootElement);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowOperationBatch.SchemaInvalid, result.Error?.Code);
        // The failing operation is identified, and the whole batch is refused.
        Assert.StartsWith("Operation 1 (", result.Error!.Message);
        Assert.Contains(expected, result.Error.Message);
        Assert.Contains("nothing changed", result.Error.Message);
    }

    [Fact]
    public async Task An_absent_activity_type_is_a_clarification_that_names_the_missing_capability()
    {
        var result = await PrepareAsync(new { kind = "add", @ref = "mail", activityTypeKey = "Acme.SendSms", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot });

        Clarification(result, "Acme.SendSms");
    }

    [Fact]
    public async Task A_present_but_non_addable_activity_is_a_clarification_with_the_availability_reason()
    {
        var result = await PrepareAsync(new { kind = "add", @ref = "old", activityTypeKey = "Acme.version-excluded", version = "2.0.0", scopePath = new[] { "flow" }, slot = FlowSlot });

        Clarification(result, "Excluded by the effective activity availability policy");
    }

    [Fact]
    public async Task An_existing_non_addable_activity_stays_editable()
    {
        var result = await PrepareAsync(
            new { kind = "set-property", activity = new { id = "legacy" }, property = "Text", value = new { literal = "still editable" } },
            new { kind = "update", activity = new { id = "legacy" }, fields = new { description = "Kept." } });

        Assert.True(result.Succeeded, result.Error?.Message);
    }

    [Fact]
    public async Task An_undeclared_property_is_a_clarification_listing_the_declared_inputs()
    {
        var result = await PrepareAsync(new { kind = "set-property", activity = new { id = "write-1" }, property = "Value", value = new { literal = "x" } });

        Clarification(result, "declares no input 'Value'");
        Assert.Contains("'Text'", result.Error!.Message);
    }

    [Fact]
    public async Task An_ambiguous_name_is_a_clarification_listing_candidates_by_scope_path()
    {
        var result = await PrepareAsync(new { kind = "remove", activity = new { name = "write line" } });

        Clarification(result, "matches 2 activities");
        Assert.Contains("'write-1' at 'Main flow' [flow] / 'Write line' [write-1]", result.Error!.Message);
        Assert.Contains("'write-2' at", result.Error.Message);
    }

    [Fact]
    public async Task A_literal_on_a_credential_input_is_refused_with_an_offer_to_bind_a_visible_Secret_Reference()
    {
        var result = await PrepareAsync(new { kind = "set-property", activity = new { id = "write-1" }, property = "ApiKey", value = new { literal = "CANARY-SECRET-5521" } });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowOperationBatch.CredentialLiteral, result.Error?.Code);
        Assert.Contains("'smtp-key'", result.Error!.Message);
        Assert.DoesNotContain("CANARY-SECRET-5521", result.Error.Message);
    }

    [Fact]
    public async Task A_Secret_Reference_that_is_not_visible_is_a_clarification()
    {
        var result = await PrepareAsync(new { kind = "set-property", activity = new { id = "write-1" }, property = "ApiKey", value = new { secretReference = new { name = "someone-elses" } } });

        Clarification(result, "not visible");
    }

    [Fact]
    public async Task An_expression_edit_is_refused_without_an_owning_Expression_Module_tool()
    {
        var result = await PrepareAsync(new { kind = "set-property", activity = new { id = "write-1" }, property = "Text", value = new { expression = new { type = "JavaScript", value = "1" } } });

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowOperationBatch.ExpressionUnsupported, result.Error?.Code);
    }

    [Theory]
    [InlineData("historical", WorkflowOperationBatch.NotEditable)]
    [InlineData(null, WorkflowOperationBatch.NotEditable)]
    public async Task A_historical_or_unmarked_working_state_is_refused_and_never_retargeted(string? workingState, string code)
    {
        var result = await PrepareAsync(Invocation(Snapshot(workingState: workingState), new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 2 }));

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Error?.Code);
        Assert.Contains("current draft", result.Error!.Message);
    }

    [Fact]
    public async Task A_snapshot_without_structure_or_from_another_generation_is_refused()
    {
        var withoutStructure = Snapshot(mutate: activities =>
        {
            foreach (var activity in activities)
                foreach (var key in new[] { "activityVersionId", "parentId", "slot", "slots" }) activity.Remove(key);
        });
        var unstructured = await PrepareAsync(Invocation(withoutStructure, new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 2 }));
        var stale = await PrepareAsync(Invocation(Snapshot(), new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 2 }) with { Client = new("client-1", "older-generation") });

        Assert.Equal(WorkflowOperationBatch.StructureRequired, unstructured.Error?.Code);
        Assert.Equal("weaver.workflow.client_mismatch", stale.Error?.Code);
    }

    [Theory]
    [InlineData(false, true, "weaver.workflow.permission_denied", 403)]
    [InlineData(true, false, "weaver.workflow.permission_denied", 403)]
    [InlineData(true, null, "workflow.authorization.unavailable", 503)]
    public async Task Without_manage_permission_the_shared_denied_outcome_is_returned_before_any_validation(bool host, bool? owner, string code, int status)
    {
        var tool = Tool(host, owner);
        // An invalid batch: an actor without manage learns nothing from validation.
        var result = await tool.PrepareAsync(Invocation(Snapshot(), new { kind = "remove", activity = new { name = "write line" } }));

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(status, result.Error?.StatusCode);
        if (status == 403) Assert.Contains("Explaining it remains available", result.Error!.Message);
    }

    [Theory]
    [InlineData("forward-ref")]
    [InlineData("removed-scope")]
    [InlineData("reused-id")]
    [InlineData("dangling-root")]
    [InlineData("remove-added")]
    [InlineData("wrong-scope-chain")]
    [InlineData("cross-scope-connect")]
    [InlineData("missing-connection")]
    [InlineData("unknown-slot")]
    public async Task Dangling_or_inconsistent_references_refuse_the_whole_batch(string defect)
    {
        object[] operations = defect switch
        {
            "forward-ref" => [new { kind = "position", activity = new { @ref = "later" }, x = 1, y = 1 },
                new { kind = "add", @ref = "later", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot }],
            "removed-scope" => [new { kind = "remove", activity = new { id = "seq" } },
                new { kind = "add", @ref = "inside", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow", "seq" }, slot = SequenceSlot }],
            "reused-id" => [new { kind = "remove", activity = new { id = "write-2" } },
                new { kind = "add", @ref = "write-2", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot }],
            "dangling-root" => [new { kind = "add", @ref = "new-root", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" }],
            "remove-added" => [new { kind = "add", @ref = "temp", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot },
                new { kind = "remove", activity = new { @ref = "temp" } }],
            "wrong-scope-chain" => [new { kind = "add", @ref = "temp", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "seq" }, slot = SequenceSlot }],
            "cross-scope-connect" => [new { kind = "connect", source = new { id = "write-1" }, target = new { id = "send" } }],
            "missing-connection" => [new { kind = "disconnect", source = new { id = "write-2" }, target = new { id = "write-1" } }],
            _ => [new { kind = "add", @ref = "temp", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = "no-such-slot" }]
        };

        var result = await PrepareAsync(operations);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Error!.Code, new[] { WorkflowOperationBatch.ReferenceInvalid, WorkflowOperationBatch.ClarificationRequired });
        Assert.Contains("nothing changed", result.Error.Message);
    }

    [Fact]
    public async Task A_new_root_replaces_the_existing_tree_destructively_and_is_filled_in_a_following_batch()
    {
        var result = await PrepareAsync(
            new { kind = "add", @ref = "new-root", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" },
            new { kind = "set-root", activity = new { @ref = "new-root" } });
        // The catalog does not disclose a new activity's slots, so nothing is added into it in the same batch.
        var filled = await PrepareAsync(
            new { kind = "add", @ref = "new-root", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" },
            new { kind = "set-root", activity = new { @ref = "new-root" } },
            new { kind = "add", @ref = "first", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "new-root" }, slot = FlowSlot });
        var twice = await PrepareAsync(
            new { kind = "add", @ref = "one", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" },
            new { kind = "add", @ref = "two", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" },
            new { kind = "set-root", activity = new { @ref = "one" } },
            new { kind = "set-root", activity = new { @ref = "two" } });

        Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, filled.Error?.Code);
        Assert.StartsWith("Operation 2 (add)", filled.Error!.Message);
        Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, twice.Error?.Code);
        Assert.Contains("at most one root", twice.Error!.Message);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(AgentRisk.Destructive, result.Value!.Risk);
        Assert.True(result.Value.Arguments.GetProperty("operations")[1].GetProperty("replacesRoot").GetBoolean());
        // The replaced tree is gone for the rest of the batch.
        var after = await PrepareAsync(
            new { kind = "add", @ref = "new-root", activityTypeKey = "Acme.version-flowchart", version = "1.0.0", scopePath = Array.Empty<string>(), slot = "root" },
            new { kind = "set-root", activity = new { @ref = "new-root" } },
            new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 1 });
        Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, after.Error?.Code);
    }

    [Fact]
    public async Task A_BPMN_scope_is_refused_at_preparation_as_the_applier_refuses_it()
    {
        var bpmn = Snapshot(mutate: activities => activities[0]["slots"] = new object[] { new { id = FlowSlot, mode = "bpmn", cardinality = "many" } });
        var add = await PrepareAsync(Invocation(bpmn, new { kind = "add", @ref = "x", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot }));
        var remove = await PrepareAsync(Invocation(bpmn, new { kind = "remove", activity = new { id = "write-2" } }));

        Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, add.Error?.Code);
        Assert.Contains("BPMN", add.Error!.Message);
        Assert.Equal(WorkflowOperationBatch.ReferenceInvalid, remove.Error?.Code);
        Assert.Contains("BPMN", remove.Error!.Message);
    }

    [Fact]
    public async Task Above_the_declared_limit_a_batch_is_labelled_high_complexity_and_above_the_bound_refused_whole()
    {
        var large = Enumerable.Range(0, WorkflowOperationBatch.ComplexityLimit + 1)
            .Select(index => (object)new { kind = "position", activity = new { id = "write-1" }, x = index, y = index }).ToArray();
        var tooLarge = Enumerable.Range(0, WorkflowOperationBatch.MaxOperations + 1)
            .Select(index => (object)new { kind = "position", activity = new { id = "write-1" }, x = index, y = index }).ToArray();

        var labelled = await PrepareAsync(large);
        var refused = await PrepareAsync(tooLarge);

        Assert.True(labelled.Succeeded, labelled.Error?.Message);
        Assert.True(labelled.Value!.Preview.Data.GetProperty("highComplexity").GetBoolean());
        Assert.Equal(large.Length, labelled.Value.Arguments.GetProperty("operations").GetArrayLength());
        Assert.Equal(WorkflowOperationBatch.SchemaInvalid, refused.Error?.Code);
        Assert.Contains("never split", refused.Error!.Message);
    }

    [Fact]
    public async Task A_catalog_the_actor_may_not_read_is_a_clarification_for_new_activities_only()
    {
        var catalog = new FixtureActivityCatalog { Allowed = false, Entries = Entries() };
        var tool = Tool(catalog: catalog);

        var add = await tool.PrepareAsync(Invocation(Snapshot(), new { kind = "add", @ref = "x", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot }));
        var move = await tool.PrepareAsync(Invocation(Snapshot(), new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 2 }));

        Clarification(add, "not visible");
        Assert.True(move.Succeeded, move.Error?.Message);
    }

    [Fact]
    public async Task The_server_never_executes_the_batch()
    {
        var tool = Tool();
        var invocation = Invocation(Snapshot(), new { kind = "position", activity = new { id = "write-1" }, x = 1, y = 2 });
        var preparation = await tool.PrepareAsync(invocation);
        var result = await tool.ExecuteAsync(AgentTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!));

        Assert.Equal("weaver.workflow.client_execution_required", result.Error?.Code);
    }

    [Fact]
    public async Task The_observed_batch_becomes_a_local_receipt_carrying_the_servers_own_risk_labels()
    {
        var (tool, prepared) = await PreparedAsync();

        var result = await tool.CompleteAsync(prepared, Acknowledgement(prepared));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(AgentEffectScope.ClientWorkingState, result.Value!.Receipt?.Scope);
        Assert.Equal("sha256:after", result.Value.Receipt?.After.Revision);
        Assert.Contains("has not been saved or published", result.Value.Content);
        var data = result.Value.Payload!.Data;
        Assert.Equal("destructive", data.GetProperty("risk").GetString());
        Assert.Equal(["reviewRequired", "destructive"], data.GetProperty("operations").EnumerateArray().Select(item => item.GetProperty("risk").GetString()));
        Assert.DoesNotContain("forged", data.GetRawText());
    }

    [Theory]
    [InlineData("count")]
    [InlineData("added")]
    [InlineData("history")]
    [InlineData("base")]
    [InlineData("same-revision")]
    [InlineData("refused")]
    [InlineData("generation")]
    public async Task An_inexact_observation_never_claims_the_batch(string defect)
    {
        var (tool, prepared) = await PreparedAsync();
        var ack = Acknowledgement(prepared, defect);
        ack = defect switch
        {
            "same-revision" => ack with { After = ack.After! with { Revision = prepared.Target!.Revision } },
            "refused" => ack with { Outcome = AgentClientOutcome.Refused },
            "generation" => ack with { Client = new("client-1", "other") },
            _ => ack
        };

        var result = await tool.CompleteAsync(prepared, ack);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value?.Receipt);
    }

    [Fact]
    public async Task Through_the_engine_a_batch_is_a_proposal_awaiting_review_even_in_full_auto()
    {
        using var root = AgentTestServices.Create(services =>
        {
            new WeaverWorkflowsFeature().ConfigureServices(services);
            services.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner(true));
            services.AddSingleton<IActivityAuthoringCatalog>(new FixtureActivityCatalog { Entries = Entries() });
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var policy = AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.FullAuto };
        var session = await services.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1",
            "test-provider", "build", null, policy, new Dictionary<string, string>()));
        var context = await services.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            WorkflowAgentTests.Request(Snapshot()) with { SessionId = session.Id });
        Assert.True(context.Succeeded, context.Error?.Message);
        var arguments = new Dictionary<string, object?> { ["operations"] = JsonSerializer.SerializeToElement(new object[] { new { kind = "position", activity = new { id = "write-1" }, x = 5, y = 6 } }) };

        var dispatch = await services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(new("batch-call", WorkflowApplyOperationBatchTool.ToolName,
            session.Id, "actor-1", arguments, context.Value!.Attachments) { TenantId = "tenant-1", TurnId = "turn-1", Client = Snapshot().Client }, session.Policy);

        Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
        Assert.True(dispatch.Proposal!.RequiresApproval);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, dispatch.Proposal.Status);
        Assert.Equal(AgentExecutionLocation.ClientWorkingState, dispatch.Proposal.PreparedInvocation!.ExecutionLocation);
        Assert.Equal(WorkflowApplyOperationBatchTool.RendererId, dispatch.Proposal.ResultRendererId);
    }

    [Fact]
    public async Task Malformed_structure_is_not_admitted()
    {
        var malformed = new[]
        {
            Snapshot(mutate: activities => activities[1]["parentId"] = "nowhere"),
            Snapshot(mutate: activities => { activities[1]["parentId"] = null; activities[1]["slot"] = null; }),
            Snapshot(mutate: activities => activities[1]["slot"] = "no-such-slot"),
            Snapshot(mutate: activities => activities[1].Remove("activityVersionId")),
            // Compact-form defects: an index out of range, an index in two slots, a single slot holding two, a container whose
            // declared parent is not where it is listed, and an unknown field.
            Snapshot(mutateStructure: structure => structure["versions"]![0]!["nodes"]!.AsArray().Add(99)),
            Snapshot(mutateStructure: structure => structure["containers"]![1]!["slots"]![0]!["nodes"]!.AsArray().Add(1)),
            Snapshot(mutateStructure: structure => structure["containers"]![0]!["slots"]![0]!["cardinality"] = "single"),
            Snapshot(mutateStructure: structure => structure["containers"]![1]!["parentId"] = "write-1"),
            Snapshot(mutateStructure: structure => structure["extra"] = true)
        };

        foreach (var snapshot in malformed)
            Assert.False(WorkflowAgentSnapshot.TryRead(snapshot, out _, out _));
        Assert.True(WorkflowAgentSnapshot.TryRead(Snapshot(), out var admitted, out _));
        Assert.Equal(6, admitted.Structure!.Count);
        Assert.Equal(("seq", SequenceSlot), (admitted.Structure["send"].ParentId, admitted.Structure["send"].Slot));
        Assert.Equal("version-excluded", admitted.Structure["legacy"].ActivityVersionId);
        // The admitted projection keeps the compact form, so a re-read (as tools and re-admission do) still fits and agrees.
        var readmitted = Snapshot() with { Content = admitted.Content };
        Assert.True(WorkflowAgentSnapshot.TryRead(readmitted, out var again, out _));
        Assert.Equal(admitted.Structure.Values.OrderBy(node => node.Id).Select(node => (node.Id, node.ParentId, node.Slot, node.ActivityVersionId)),
            again.Structure!.Values.OrderBy(node => node.Id).Select(node => (node.Id, node.ParentId, node.Slot, node.ActivityVersionId)));
        await Task.CompletedTask;
    }

    // ---- Fixtures ----

    private static void Clarification(AgentResult<AgentToolPreparation> result, string expected)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowOperationBatch.ClarificationRequired, result.Error?.Code);
        Assert.Contains(expected, result.Error!.Message);
        Assert.Contains("nothing changed", result.Error.Message);
    }

    private static Task<AgentResult<AgentToolPreparation>> PrepareAsync(params object[] operations)
        => Tool().PrepareAsync(Invocation(Snapshot(), operations));

    private static Task<AgentResult<AgentToolPreparation>> PrepareAsync(JsonElement arguments)
        => Tool().PrepareAsync(Invocation(Snapshot(), []) with
        {
            Arguments = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
        });

    private static Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation) => Tool().PrepareAsync(invocation);

    private static async Task<(WorkflowApplyOperationBatchTool, AgentPreparedToolInvocation)> PreparedAsync()
    {
        var tool = Tool();
        var invocation = Invocation(Snapshot(),
            new { kind = "add", @ref = "greet", activityTypeKey = "Acme.version-write-line", version = "1.0.0", scopePath = new[] { "flow" }, slot = FlowSlot },
            new { kind = "remove", activity = new { id = "write-2" } });
        var preparation = await tool.PrepareAsync(invocation);
        Assert.True(preparation.Succeeded, preparation.Error?.Message);
        return (tool, AgentTestToolHelpers.Prepared(invocation, tool.Descriptor, preparation.Value!));
    }

    private static AgentClientAcknowledgement Acknowledgement(AgentPreparedToolInvocation invocation, string? defect = null)
        => new(invocation.Client!, invocation.ApprovalDigest, AgentClientOutcome.Applied, "Untrusted summary", invocation.Target! with { Revision = "sha256:after" },
            new(WorkflowOperationBatch.ResultType, "1", JsonSerializer.SerializeToElement(new
            {
                observed = true, revision = "sha256:after", baseRevision = defect == "base" ? "other" : invocation.Target!.Revision,
                appliedCount = defect == "count" ? 1 : 2, addedNodeIds = defect == "added" ? new[] { "other" } : new[] { "greet" },
                historyEntry = defect != "history", undoToken = "undo-1", risk = "forged", operations = "forged"
            })));

    private static WorkflowApplyOperationBatchTool Tool(bool host = true, bool? owner = true, FixtureActivityCatalog? catalog = null)
        => new(new HostAuthority(host), [new Owner(owner)], [catalog ?? new FixtureActivityCatalog { Entries = Entries() }]);

    private static AgentToolInvocation Invocation(AgentContextAttachment snapshot, params object[] operations)
        => new("call-1", WorkflowApplyOperationBatchTool.ToolName, "session-1", "actor-1",
            new Dictionary<string, object?> { ["operations"] = JsonSerializer.SerializeToElement(operations) }, [snapshot])
        { TenantId = "tenant-1", TurnId = "turn-1", Client = snapshot.Client };

    internal static IReadOnlyList<ActivityAuthoringCatalogEntry> Entries() =>
    [
        FixtureActivityCatalog.Entry("version-write-line", true,
            new ActivityAuthoringCatalogInput("Text", "Text", "String", "Text", true, false, false),
            new ActivityAuthoringCatalogInput("ApiKey", "ApiKey", "String", "API key", false, true, true),
            new ActivityAuthoringCatalogInput("CanStartWorkflow", "CanStartWorkflow", "Boolean", "Can start workflow", false, false, false)),
        FixtureActivityCatalog.Entry("version-flowchart", true),
        FixtureActivityCatalog.Entry("version-sequence", true),
        FixtureActivityCatalog.Entry("version-send", true),
        FixtureActivityCatalog.Entry("version-excluded", false, new ActivityAuthoringCatalogInput("Text", "Text", "String", "Text", false, false, false))
    ];

    /// <summary>
    /// A structured working draft: a root flowchart with two same-named Write Line nodes, a nested sequence holding one
    /// activity, and an existing activity whose version is no longer addable.
    /// </summary>
    internal static AgentContextAttachment Snapshot(string? workingState = "draft", Action<List<Dictionary<string, object?>>>? mutate = null,
        Action<JsonObject>? mutateStructure = null)
    {
        static object[] Slots(string id, string mode) => [new { id, mode, cardinality = "many" }];
        // Authored per activity for readability, then sent in the editor's compact form (Compact below).
        var activities = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = "flow", ["type"] = "Acme.version-flowchart", ["displayName"] = "Main flow", ["activityVersionId"] = "version-flowchart", ["parentId"] = null, ["slot"] = null, ["slots"] = Slots(FlowSlot, "flowchart") },
            new() { ["id"] = "write-1", ["type"] = "Acme.version-write-line", ["displayName"] = "Write line", ["activityVersionId"] = "version-write-line", ["parentId"] = "flow", ["slot"] = FlowSlot, ["slots"] = Array.Empty<object>() },
            new() { ["id"] = "write-2", ["type"] = "Acme.version-write-line", ["displayName"] = "Write Line", ["activityVersionId"] = "version-write-line", ["parentId"] = "flow", ["slot"] = FlowSlot, ["slots"] = Array.Empty<object>() },
            new() { ["id"] = "seq", ["type"] = "Acme.version-sequence", ["displayName"] = "Inner sequence", ["activityVersionId"] = "version-sequence", ["parentId"] = "flow", ["slot"] = FlowSlot, ["slots"] = Slots(SequenceSlot, "sequence") },
            new() { ["id"] = "send", ["type"] = "Acme.version-send", ["displayName"] = "Send email", ["activityVersionId"] = "version-send", ["parentId"] = "seq", ["slot"] = SequenceSlot, ["slots"] = Array.Empty<object>() },
            new() { ["id"] = "legacy", ["type"] = "Acme.version-excluded", ["displayName"] = "Legacy step", ["activityVersionId"] = "version-excluded", ["parentId"] = "flow", ["slot"] = FlowSlot, ["slots"] = Array.Empty<object>() }
        };
        mutate?.Invoke(activities);
        var content = new Dictionary<string, object?>
        {
            ["workflowDefinitionId"] = "wf-1", ["draftId"] = "draft-1", ["revision"] = "sha256:before", ["selectedNodeId"] = "write-1",
            ["activities"] = activities.Select(activity => new { id = activity["id"], type = activity["type"], displayName = activity["displayName"] }).ToArray(),
            ["connections"] = new[] { new { source = "write-1", target = "write-2", sourcePort = "Done" } },
            ["positions"] = new[] { new { nodeId = "write-1", x = 10, y = 20 } },
            ["secretReferences"] = new[] { new { name = "smtp-key", typeName = "ApiKey", scope = "tenant" } }
        };
        if (Compact(activities) is { } structure)
        {
            mutateStructure?.Invoke(structure);
            content["structure"] = structure;
        }
        if (workingState is not null) content["workingState"] = workingState;
        return new("ctx-1", "workflow.definition", "wf-1", "Workflow", "workflow.definition", AgentContextSensitivity.Internal, "selection", "Workflow data",
            JsonSerializer.SerializeToElement(content), new Dictionary<string, string>())
        {
            ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Origin = AgentContextOrigin.ClientSnapshot,
            Target = new(WorkflowAgentSnapshot.ModuleId, "workflow-definition", "wf-1", "sha256:before"), Client = new("client-1", "generation-1")
        };
    }

    /// <summary>The editor's compact structure: activity indexes grouped by version, and each container's slots by index.</summary>
    private static JsonObject? Compact(List<Dictionary<string, object?>> activities)
    {
        if (activities.All(activity => !activity.ContainsKey("activityVersionId"))) return null;
        var versions = new JsonArray();
        foreach (var group in activities.Select((activity, index) => (activity, index)).Where(item => item.activity.ContainsKey("activityVersionId"))
                     .GroupBy(item => (string)item.activity["activityVersionId"]!))
            versions.Add(new JsonObject { ["id"] = group.Key, ["nodes"] = new JsonArray(group.Select(item => (JsonNode)item.index).ToArray()) });
        var containers = new JsonArray();
        foreach (var activity in activities.Where(activity => activity.ContainsKey("slots") && (activity["parentId"] is null || ((object[])activity["slots"]!).Length > 0)))
        {
            var slots = new JsonArray();
            foreach (var slot in (object[])activity["slots"]!)
            {
                var id = (string)slot.GetType().GetProperty("id")!.GetValue(slot)!;
                var members = activities.Select((child, index) => (child, index))
                    .Where(item => item.child.GetValueOrDefault("parentId") as string == (string)activity["id"]! && item.child.GetValueOrDefault("slot") as string == id)
                    .Select(item => (JsonNode)item.index).ToArray();
                slots.Add(new JsonObject { ["id"] = id, ["mode"] = (string)slot.GetType().GetProperty("mode")!.GetValue(slot)!, ["cardinality"] = "many", ["nodes"] = new JsonArray(members) });
            }
            containers.Add(new JsonObject { ["id"] = (string)activity["id"]!, ["parentId"] = activity["parentId"] as string, ["slot"] = activity["slot"] as string, ["slots"] = slots });
        }
        return new JsonObject { ["versions"] = versions, ["containers"] = containers };
    }

    private sealed class HostAuthority(bool allowed) : IAgentToolAuthorizer
    {
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentPolicyDecision(allowed, allowed ? [] : [new("unit.denied", "Host authority withheld the tool.")]));
    }

    /// <summary>The owning workflow authorizer: null manage means it cannot decide.</summary>
    private sealed class Owner(bool? manage) : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(definitionId == "wf-1");
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => manage is null
            ? throw new WorkflowDefinitionAuthorizationException(definitionId, "manage", new InvalidOperationException("owner offline"))
            : ValueTask.FromResult(manage.Value && definitionId == "wf-1");
    }
}
