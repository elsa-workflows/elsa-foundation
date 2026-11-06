using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Dashboard.Tests;

public sealed class DashboardWeaverTests
{
    [Fact]
    public async Task Real_turn_proposes_then_previews_as_canonical_viewer_without_saving_or_disclosing_measured_data()
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var events = await ReadAsync(host.Authoring.AuthorAsync(new("Show records and amounts", DashboardWeaverHost.Draft())));
        Assert.DoesNotContain(events, item => item.Kind == "failed");
        var proposal = Assert.Single(events, item => item.Kind == "proposal").Proposal!;
        Assert.Equal(FixtureSource.RowCanary, proposal.Preview!.Rows[0].Values["id"].Value);
        Assert.Equal(FixtureSource.ValueCanary, proposal.Preview.Rows[0].Values["amount"].Value);
        Assert.Equal("unknown", proposal.Preview.Coverage.Status);
        Assert.Equal(1, host.Source.ExecutionCalls);
        Assert.Equal("canonical-user", Assert.Single(host.Source.ExecutionScopes).SubjectId);
        Assert.Equal("owned-test-host", host.Source.ExecutionScopes[0].HostId);
        Assert.Equal("external-subject", Assert.Single(host.Model.Created).ActorId);
        Assert.True(host.Model.Seen.Count >= 2);
        Assert.Contains(host.Model.Seen[0].AvailableTools, tool => tool.Name == "dashboard_preview_question");
        var allProviderInput = JsonSerializer.Serialize(host.Model.Seen, DashboardWeaverProtocol.Json);
        Assert.DoesNotContain(FixtureSource.RowCanary, allProviderInput);
        Assert.DoesNotContain(FixtureSource.ValueCanary, allProviderInput);
        Assert.Contains(DashboardWeaverProtocol.ProposalType, allProviderInput);
        Assert.Empty((await host.Resources.ListAsync(host.Actors.Actor, "question")).Items);
        Assert.Empty((await host.Resources.ListAsync(host.Actors.Actor, "dashboard")).Items);
        var again = await ReadAsync(host.Authoring.AuthorAsync(new("Refine this question", DashboardWeaverHost.Draft(), events[0].ConversationId)));
        Assert.DoesNotContain(again, item => item.Kind == "failed");
        Assert.DoesNotContain(FixtureSource.RowCanary, JsonSerializer.Serialize(host.Model.Seen, DashboardWeaverProtocol.Json));
    }

    [Fact]
    public async Task Provider_unavailable_preserves_native_prepare_and_does_not_create_conversation_or_execute()
    {
        await using var host = new DashboardWeaverHost(providerAvailable: false);
        await host.InitializeAsync();
        Assert.Single((await host.Authoring.PrepareAsync(DashboardWeaverHost.Draft(DashboardWeaverHost.Question()))).Datasets);
        var result = Assert.Single(await ReadAsync(host.Authoring.AuthorAsync(new("Show records", DashboardWeaverHost.Draft()))));
        Assert.Equal("dashboards.provider.unavailable", result.Code);
        Assert.Empty(host.Model.Created); Assert.Empty(host.Model.Seen); Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_editor_null_or_exact_native_blank_can_start_a_real_authoring_turn(bool nativeBlank)
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var draft = DashboardWeaverHost.Draft(nativeBlank ? NativeBlankQuestion() : null);
        var context = await host.Authoring.PrepareAsync(draft);
        Assert.Single(context.Datasets);
        var events = await ReadAsync(host.Authoring.AuthorAsync(new("Compose a supported question", draft)));
        Assert.DoesNotContain(events, item => item.Kind == "failed");
        Assert.Single(events, item => item.Kind == "proposal");
        Assert.NotEmpty(host.Model.Seen);
        var initial = DashboardWeaverProtocol.ReadDraft(host.Model.Seen[0].Context.Single(item => item.ContentType == DashboardWeaverProtocol.ContextKind));
        Assert.Equal(nativeBlank, initial.Definition is not null);
        if (nativeBlank) Assert.Equal("", initial.Definition!.Query.Dataset.Id);
        Assert.DoesNotContain(FixtureSource.RowCanary, JsonSerializer.Serialize(host.Model.Seen, DashboardWeaverProtocol.Json));
        Assert.DoesNotContain(FixtureSource.ValueCanary, JsonSerializer.Serialize(host.Model.Seen, DashboardWeaverProtocol.Json));
    }

    [Theory]
    [InlineData("old-question")]
    [InlineData("filter")]
    [InlineData("parameter-value")]
    [InlineData("parameter-definition")]
    [InlineData("metric")]
    [InlineData("group")]
    [InlineData("sort")]
    [InlineData("time")]
    [InlineData("comparison")]
    [InlineData("having")]
    [InlineData("visualization")]
    [InlineData("schema")]
    [InlineData("form")]
    [InlineData("limit")]
    [InlineData("whitespace-id")]
    [InlineData("saved-target")]
    [InlineData("null-query")]
    [InlineData("null-values")]
    public async Task Unselected_populated_or_malformed_draft_is_refused_before_persistence_and_provider_disclosure(string variation)
    {
        await using var host = new DashboardWeaverHost();
        var blank = NativeBlankQuestion();
        var canary = new DashboardValue("string", FixtureSource.RowCanary);
        var question = variation switch
        {
            "old-question" => DashboardWeaverHost.Question() with { Query = DashboardWeaverHost.Question().Query with { Dataset = new("", "") } },
            "filter" => blank with { Query = blank.Query with { Filters = new() { Field = "withdrawn-business-field", Operator = "eq", Value = canary } } },
            "parameter-value" => blank with { ParameterValues = new Dictionary<string, DashboardValue> { ["withdrawn"] = canary } },
            "parameter-definition" => blank with { Query = blank.Query with { Parameters = [new("withdrawn", "string", Default: canary)] } },
            "metric" => blank with { Query = blank.Query with { Metrics = [new("secret", "withdrawn-metric", "v1")] } },
            "group" => blank with { Query = blank.Query with { GroupBy = ["withdrawn-business-field"] } },
            "sort" => blank with { Query = blank.Query with { Sort = [new("withdrawn-business-field")] } },
            "time" => blank with { Query = blank.Query with { Time = new() { Field = "withdrawn-instant", Window = "thisWeek" } } },
            "comparison" => blank with { Query = blank.Query with { Comparison = new("previousPeriod") } },
            "having" => blank with { Query = blank.Query with { Having = new() { Metric = "withdrawn", Operator = "eq", Value = canary } } },
            "visualization" => blank with { Visualization = new() { Label = FixtureSource.RowCanary } },
            "schema" => blank with { Query = blank.Query with { Dataset = new("", "old-schema") } },
            "form" => blank with { Query = blank.Query with { Form = "records" } },
            "limit" => blank with { Query = blank.Query with { Limit = 100 } },
            "whitespace-id" => blank with { Query = blank.Query with { Dataset = new(" ", "") } },
            "null-query" => blank with { Query = null! },
            "null-values" => blank with { ParameterValues = null! },
            _ => blank
        };
        var draft = DashboardWeaverHost.Draft(question);
        if (variation == "saved-target") draft = draft with { Target = draft.Target with { ResourceType = "question" } };
        var session = await host.SessionAsync();
        var failure = await Assert.ThrowsAsync<DashboardResourceException>(() => ReadAsync(
            host.Authoring.AuthorAsync(new("Continue this draft", draft, session.Id))));
        Assert.Equal("validation", failure.Code);
        // A wire null must stay null: the installed protocol omits null fields when writing
        // trusted typed objects, which would otherwise turn this malformed input into defaults.
        var attachment = DashboardWeaverProtocol.ClientAttachment(draft) with
            { Content = JsonSerializer.SerializeToElement(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        var admission = await host.Collector.CollectAsync(session.Policy,
            new(session.Id, "dashboard-question", new Dictionary<string, string>())
            {
                CallerAttachments = [attachment], Client = DashboardWeaverProtocol.Client(draft),
                Target = DashboardWeaverProtocol.Target(draft.Target)
            });
        Assert.False(admission.Succeeded);
        Assert.Empty(await host.Sessions.ListMessagesAsync(session.Id));
        Assert.Empty(await host.Sessions.ListContextAsync(session.Id));
        Assert.Empty(host.Model.Created);
        Assert.Empty(host.Model.Seen);
        Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Theory]
    [InlineData("canonical-user", "tenant-a")]
    [InlineData("external-subject", "other-tenant")]
    public async Task Current_context_refuses_session_with_wrong_authentication_subject_or_tenant(string subject, string tenant)
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var session = await host.SessionAsync(subject, tenant);
        var result = await host.CollectAsync(session, DashboardWeaverHost.Draft());
        Assert.False(result.Succeeded); Assert.Equal(0, host.Source.ExecutionCalls); Assert.Empty(host.Model.Seen);
    }

    [Theory]
    [InlineData("other-editor", "generation-1")]
    [InlineData("editor-1", "older-generation")]
    public async Task Real_collector_rejects_wrong_editor_binding(string client, string generation)
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var result = await host.CollectAsync(await host.SessionAsync(), DashboardWeaverHost.Draft(), new(client, generation));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Real_collector_rejects_attachment_for_another_requested_resource()
    {
        await using var host = new DashboardWeaverHost();
        var draft = DashboardWeaverHost.Draft();
        var session = await host.SessionAsync();
        var result = await host.Collector.CollectAsync(session.Policy, new(session.Id, "dashboard-question", new Dictionary<string, string>())
        {
            CallerAttachments = [DashboardWeaverProtocol.ClientAttachment(draft)], Client = DashboardWeaverProtocol.Client(draft),
            Target = DashboardWeaverProtocol.Target(draft.Target) with { ResourceId = "another-question" }
        });
        Assert.False(result.Succeeded); Assert.Empty(host.Model.Seen);
    }

    [Fact]
    public async Task Missing_authentication_mapping_fails_closed_before_source_discovery()
    {
        await using var host = new DashboardWeaverHost();
        host.Actors.Actor = host.Actors.Actor with { AuthenticationSubjectId = null };
        var error = await Assert.ThrowsAsync<DashboardResourceException>(() => host.Authoring.PrepareAsync(DashboardWeaverHost.Draft()).AsTask());
        Assert.Equal("denied", error.Code);
    }

    [Theory]
    [InlineData("studio/questions:read")]
    [InlineData("studio/questions:create")]
    public async Task Native_authoring_entry_requires_current_coarse_read_and_draft_create_rights(string permission)
    {
        await using var host = new DashboardWeaverHost();
        host.Actors.Actor = host.Actors.Actor with { Permissions = new HashSet<string>([permission]) };
        if (permission == "studio/questions:read")
        {
            var context = await host.Authoring.PrepareAsync(DashboardWeaverHost.Draft());
            Assert.False(context.CanEdit); Assert.False(context.CanSave);
        }
        else Assert.Equal("denied", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.Authoring.PrepareAsync(DashboardWeaverHost.Draft()).AsTask())).Code);
        var failure = await Assert.ThrowsAsync<DashboardResourceException>(() => ReadAsync(host.Authoring.AuthorAsync(new("Author a question", DashboardWeaverHost.Draft()))));
        Assert.Equal("denied", failure.Code); Assert.Empty(host.Model.Created); Assert.Empty(host.Model.Seen);
    }

    [Fact]
    public async Task Saved_read_only_context_compares_same_definition_type_and_current_acl_revision()
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var owner = host.Actors.Actor;
        var question = DashboardWeaverHost.Question() with
        {
            Query = DashboardWeaverHost.Question().Query with { Parameters = [new("a", "string", Default: new("string", "a")), new("b", "string", Default: new("string", "b"))] },
            ParameterValues = new Dictionary<string, DashboardValue> { ["a"] = new("string", "a"), ["b"] = new("string", "b") }
        };
        var saved = await host.Resources.CreateAsync(owner, "question", new() { Name = "Shared", OperationKey = "create", Definition = question });
        saved = await host.Resources.SetAudienceAsync(owner, "question", saved.Id, saved.Revision, [new("user", "viewer", "viewer")]);
        host.Actors.Actor = owner with { UserId = "viewer", AuthenticationSubjectId = "viewer-subject", Permissions = new HashSet<string>(["studio/questions:read", "studio/questions:create"]) };
        var reversed = question with { ParameterValues = new Dictionary<string, DashboardValue> { ["b"] = new("string", "b"), ["a"] = new("string", "a") } };
        var draft = DashboardWeaverHost.Draft(reversed) with { Target = new(DashboardWeaverProtocol.ModuleId, "question", saved.Id, saved.Revision) };
        var current = await host.Authoring.PrepareAsync(draft);
        Assert.False(current.CanEdit); Assert.False(current.CanSave);
        Assert.Equal("denied", (await Assert.ThrowsAsync<DashboardResourceException>(() => ReadAsync(
            host.Authoring.AuthorAsync(new("Refine this shared question", draft))))).Code);
        Assert.Empty(host.Model.Created);
        var changed = draft with { Definition = reversed with { Query = reversed.Query with { Fields = ["id"] } } };
        Assert.Equal("denied", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.Authoring.PrepareAsync(changed).AsTask())).Code);
        await host.Resources.SetAudienceAsync(owner, "question", saved.Id, saved.Revision, []);
        Assert.Equal("notFound", (await Assert.ThrowsAsync<DashboardResourceException>(() => host.Authoring.PrepareAsync(draft).AsTask())).Code);
    }

    [Theory]
    [InlineData("dashboard_discover_datasets")]
    [InlineData("dashboard_preview_question")]
    public async Task Real_invoker_executes_owned_read_only_tools_with_engine_provenance(string tool)
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        object args = tool == "dashboard_discover_datasets" ? new { datasetId = "fixture.records" } : new { question = DashboardWeaverHost.Question() };
        var invocation = await host.InvocationAsync(tool, args);
        var dispatch = await host.Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(invocation, AgentPolicy.Default);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        Assert.True(dispatch.Result!.Succeeded); Assert.NotNull(dispatch.Result.Access?.InvocationId);
        Assert.Equal(DashboardWeaverProtocol.SourceId, dispatch.Result.Access!.DisclosureSourceId);
        Assert.Equal(0, host.Source.ExecutionCalls);
        Assert.Empty((await host.Resources.ListAsync(host.Actors.Actor, "question")).Items);
    }

    [Fact]
    public async Task Generic_discovery_and_proposal_can_switch_from_selected_source_to_another_installed_source()
    {
        await using var host = new DashboardWeaverHost(new() { AdditionalDatasets = 1 });
        var draft = DashboardWeaverHost.Draft(DashboardWeaverHost.Question());
        Assert.Single((await host.Authoring.PrepareAsync(draft)).Datasets);
        var invoker = host.Services.GetRequiredService<IAgentToolInvoker>();
        var catalog = await invoker.DispatchAsync(await host.InvocationAsync("dashboard_discover_datasets", new { datasetId = "fixture.extra-1" }, draft), AgentPolicy.Default);
        Assert.Equal("fixture.extra-1", Assert.Single(catalog.Result!.Payload!.Data.Deserialize<DashboardCatalogResult>(DashboardWeaverProtocol.Json)!.Items).Id);
        var question = DashboardWeaverHost.Question();
        question = question with { Query = question.Query with { Dataset = new("fixture.extra-1", "1") } };
        var proposed = await invoker.DispatchAsync(await host.InvocationAsync("dashboard_preview_question", new { question }, draft), AgentPolicy.Default);
        Assert.Equal(AgentToolDispatchOutcome.Executed, proposed.Outcome);
        Assert.Equal("fixture.extra-1", proposed.Result!.Payload!.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json)!.Definition.Query.Dataset.Id);
        Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("dataset")]
    [InlineData("identity")]
    [InlineData("target")]
    [InlineData("client")]
    [InlineData("expired")]
    public async Task Prepared_question_revalidates_current_authority_source_and_exact_editor(string change)
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var tool = (await host.Services.GetRequiredService<IAgentToolRegistry>().FindAsync("dashboard_preview_question"))!;
        var prepared = await host.PrepareAsync(tool, await host.InvocationAsync(tool.Descriptor.Name, new { question = DashboardWeaverHost.Question() }));
        switch (change)
        {
            case "field": host.Source.AmountWithdrawn = true; break;
            case "dataset": host.Source.Hidden = true; break;
            case "identity": host.Actors.Actor = host.Actors.Actor with { AuthenticationSubjectId = "someone-else" }; break;
            case "target": prepared = prepared with { Target = prepared.Target! with { ResourceId = "other-draft" } }; break;
            case "client": prepared = prepared with { Client = new("editor-1", "old-generation") }; break;
            case "expired": prepared = prepared with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }; break;
        }
        var result = await tool.ExecuteAsync(prepared);
        Assert.False(result.Succeeded); Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Fact]
    public async Task Registered_context_budget_includes_metadata_and_guidance_but_selected_source_stays_usable()
    {
        await using var host = new DashboardWeaverHost(new() { AdditionalDatasets = 60 });
        var error = await Assert.ThrowsAsync<DashboardResourceException>(() => host.Authoring.PrepareAsync(DashboardWeaverHost.Draft()).AsTask());
        Assert.Equal("tooLarge", error.Code);
        var selected = await host.Authoring.PrepareAsync(DashboardWeaverHost.Draft(DashboardWeaverHost.Question()));
        Assert.Single(selected.Datasets);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(new[] { DashboardWeaverProtocol.Admitted(selected), DashboardWeaverProtocol.InstalledGuidance() }, DashboardWeaverProtocol.Json).Length <= DashboardWeaverProtocol.Budget);
    }

    [Fact]
    public async Task Real_collector_reports_owner_unavailable_without_leaking_source_fault_or_calling_provider()
    {
        await using var host = new DashboardWeaverHost(new() { ThrowOnDiscovery = true });
        var result = await host.CollectAsync(await host.SessionAsync(), DashboardWeaverHost.Draft());
        Assert.False(result.Succeeded); Assert.Equal(503, result.Error!.StatusCode);
        Assert.DoesNotContain("protected-database", result.Error.Message); Assert.Empty(host.Model.Seen);
    }

    [Fact]
    public async Task Incompatible_saved_schema_is_unavailable_in_authoring_instead_of_a_permission_denial()
    {
        await using var host = new DashboardWeaverHost();
        await host.InitializeAsync();
        var saved = await host.Resources.CreateAsync(host.Actors.Actor, "question", new()
            { Name = "Saved", OperationKey = "schema-check", Definition = DashboardWeaverHost.Question() });
        var database = host.Services.GetRequiredService<DashboardResourceDbContext>();
        var row = await database.Resources.SingleAsync(); row.SchemaVersion = "9.0.0"; await database.SaveChangesAsync();
        var draft = DashboardWeaverHost.Draft(DashboardWeaverHost.Question()) with
            { Target = new(DashboardWeaverProtocol.ModuleId, "question", saved.Id, saved.Revision) };
        var result = await host.CollectAsync(await host.SessionAsync(), draft);
        Assert.False(result.Succeeded); Assert.Equal(503, result.Error!.StatusCode);
        Assert.Empty(host.Model.Seen); Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Theory]
    [InlineData("catalog", "metadata")]
    [InlineData("proposal", "field")]
    [InlineData("proposal", "target")]
    [InlineData("proposal", "tool")]
    [InlineData("proposal", "contract")]
    [InlineData("proposal", "client")]
    [InlineData("proposal", "working-revision")]
    [InlineData("proposal", "rows")]
    [InlineData("proposal", "missing-owner")]
    public async Task Real_disclosure_admission_rechecks_retained_catalog_proposal_and_ownership(string kind, string change)
    {
        await using var host = new DashboardWeaverHost();
        var tool = kind == "catalog" ? "dashboard_discover_datasets" : "dashboard_preview_question";
        object args = kind == "catalog" ? new { datasetId = "fixture.records" } : new { question = DashboardWeaverHost.Question() };
        var invocation = await host.InvocationAsync(tool, args);
        var dispatch = await host.Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(invocation, AgentPolicy.Default);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        var result = dispatch.Result!;
        var original = await DisclosureAsync(host, invocation.SessionId, invocation.Context, result);
        Assert.True(original.Succeeded, original.Error?.Message);
        switch (change)
        {
            case "metadata": host.Source.Title = "Changed current title"; break;
            case "field": host.Source.AmountWithdrawn = true; break;
            case "target": result = result with { Access = result.Access! with { Target = result.Access.Target! with { ResourceId = "other-draft" } } }; break;
            case "tool": result = result with { ToolName = "dashboard_discover_datasets" }; break;
            case "contract": result = result with { Access = result.Access! with { ContractVersion = "99" } }; break;
            case "missing-owner": result = result with { Access = result.Access! with { DisclosureSourceId = "withdrawn.source" } }; break;
            case "rows":
                var proposal = result.Payload!.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json)!;
                result = result with { Payload = result.Payload with { Data = DashboardWeaverProtocol.Element(proposal with { Preview = new DashboardQueryResult() }) } };
                break;
            case "client":
            case "working-revision":
                var stale = result.Payload!.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json)!;
                stale = change == "client" ? stale with { Client = new("editor-1", "old-generation") } : stale with { WorkingRevision = "old-working-revision" };
                result = result with { Payload = result.Payload with { Data = DashboardWeaverProtocol.Element(stale) } };
                break;
        }
        var denied = await DisclosureAsync(host, invocation.SessionId, invocation.Context, result);
        Assert.False(denied.Succeeded); Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Fact]
    public async Task Retained_context_fails_if_its_registered_source_is_removed_or_client_content_is_replayed()
    {
        await using var host = new DashboardWeaverHost();
        var session = await host.SessionAsync();
        var current = (await host.CollectAsync(session, DashboardWeaverHost.Draft(DashboardWeaverHost.Question()))).Value!.Attachments;
        var missing = current.Select(item => item with { ContributorId = "removed.owner" }).ToArray();
        Assert.False((await DisclosureAsync(host, session.Id, missing)).Succeeded);
        var rebound = current.Select(item => item.ContentType == DashboardWeaverProtocol.ContextKind ?
            item with { Client = new("editor-1", "another-generation") } : item).ToArray();
        Assert.False((await DisclosureAsync(host, session.Id, rebound)).Succeeded);
        host.Source.Title = "Current changed declaration";
        Assert.False((await DisclosureAsync(host, session.Id, current)).Succeeded);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null,null]")]
    [InlineData("[{\"id\":\"a\",\"label\":\"A\",\"description\":\"A\"},{\"id\":\"a\",\"label\":\"B\",\"description\":\"B\"}]")]
    public async Task Clarification_invalid_choices_return_bounded_validation_failure(string choices)
    {
        await using var host = new DashboardWeaverHost();
        var args = JsonSerializer.Deserialize<JsonElement>("{\"question\":\"Which meaning?\",\"choices\":" + choices + "}");
        var invocation = await host.InvocationAsync("dashboard_request_clarification", args);
        var tool = (await host.Services.GetRequiredService<IAgentToolRegistry>().FindAsync(invocation.ToolName))!;
        var result = await tool.PrepareAsync(invocation);
        Assert.False(result.Succeeded); Assert.Equal(422, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Clarification_runs_through_real_turn_without_confirming_interpretation_or_executing()
    {
        await using var host = new DashboardWeaverHost();
        host.Model.Tool = "dashboard_request_clarification";
        host.Model.Arguments = new { question = "Which time meaning?", choices = new[]
            { new { id = "cohort", label = "Started in week", description = "Current outcomes of runs started this week." },
              new { id = "events", label = "Occurred in week", description = "Failure events occurring this week." } } };
        var events = await ReadAsync(host.Authoring.AuthorAsync(new("Show failures this week", DashboardWeaverHost.Draft())));
        Assert.Equal(2, Assert.Single(events, item => item.Kind == "clarification").Clarification!.Choices.Count);
        Assert.DoesNotContain(events, item => item.Kind == "proposal" || item.Kind == "failed");
        Assert.Equal(0, host.Source.ExecutionCalls);
        var contexts = host.Model.Seen.SelectMany(item => item.Context).Where(item => item.ContentType == DashboardWeaverProtocol.ContextKind);
        Assert.All(contexts, item => Assert.Null(DashboardWeaverProtocol.ReadDraft(item).ConfirmedInterpretation));
    }

    private static async Task<List<DashboardAuthoringEvent>> ReadAsync(IAsyncEnumerable<DashboardAuthoringEvent> source)
    { var result = new List<DashboardAuthoringEvent>(); await foreach (var item in source) result.Add(item); return result; }

    // Exact JSON emitted by the native initialDefinition() before a dataset is selected.
    private static DashboardQueryRequest NativeBlankQuestion() => JsonSerializer.Deserialize<DashboardQueryRequest>("""
        {"query":{"schemaVersion":1,"dataset":{"id":"","schemaRevision":""},"form":"aggregate","parameters":[],
        "fields":[],"groupBy":[],"metrics":[],"sort":[],"limit":50},"visualization":{"kind":"table","y":[]},"parameterValues":{}}
        """, DashboardWeaverProtocol.Json)!;

    private static Task<AgentResult<AgentContextContribution>> DisclosureAsync(DashboardWeaverHost host, string sessionId,
        IReadOnlyCollection<AgentContextAttachment> context, params AgentToolResult[] results) => host.Collector.CollectAsync(
        AgentPolicy.Default, new(sessionId, "dashboard-question", new Dictionary<string, string>())
        { Disclosure = new(sessionId, context, results, false) { IsHistorical = true } });
}
