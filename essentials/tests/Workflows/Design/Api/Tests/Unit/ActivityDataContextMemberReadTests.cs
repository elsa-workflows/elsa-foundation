using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Tests.Support;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// The workflow activity-data endpoint says whether member reads are on, and answers a refused source with the
/// analysis that refused it rather than with a bare code.
/// </summary>
public sealed class ActivityDataContextMemberReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_answer_says_whether_member_reads_are_on(bool memberReads)
    {
        using var host = new Host(memberReads);

        var result = await host.Invoke(host.Request());

        Assert.Equal("supported", result.Body.State);
        Assert.Equal(memberReads, result.Body.Analysis!.MemberReadsEnabled);
        Assert.Contains($"\"memberReadsEnabled\":{(memberReads ? "true" : "false")}", result.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void An_analysis_without_the_member_reads_fact_reads_as_off()
    {
        var analysis = JsonSerializer.Deserialize<ActivityDataSelectionAnalysis>(
            """{"contractRevision":"c","target":null,"sources":[],"findings":[],"hasMoreSources":false}""", Web);

        Assert.False(analysis!.MemberReadsEnabled);
    }

    [Fact]
    public async Task With_member_reads_off_a_member_path_query_is_invalid_with_its_finding_and_no_source()
    {
        using var host = new Host(memberReads: false);

        var result = await host.Invoke(host.Request() with { Query = new() { Source = ScopeFree("producer"), MemberPath = ["customerId"] } });

        Assert.Equal("invalid", result.Body.State);
        Assert.Equal("activity-data.invalid-source-or-target", result.Body.Code);
        Assert.Empty(result.Body.Analysis!.Sources);
        Assert.Equal(("consumer/inputs/CustomerId", "ActivityData/UnsupportedMemberBinding"),
            (Assert.Single(result.Body.Analysis.Findings).Path, result.Body.Analysis.Findings[0].Type));
        Assert.False(result.Body.Analysis.MemberReadsEnabled);
    }

    [Fact]
    public async Task With_member_reads_on_a_member_path_query_offers_the_member_read()
    {
        using var host = new Host(memberReads: true);

        var result = await host.Invoke(host.Request() with { Query = new() { Source = ScopeFree("producer"), MemberPath = ["customerId"] } });

        Assert.Equal("supported", result.Body.State);
        Assert.Equal("ActivityResultMember", Assert.Single(result.Body.Analysis!.Sources).Binding!.ExpressionType);
    }

    [Fact]
    public async Task A_refused_source_is_disclosed_with_its_findings_and_context_identity()
    {
        using var host = new Host(memberReads: false);

        var result = await host.Invoke(host.Request() with { Query = new() { Source = ScopeFree("missing") } });

        Assert.Equal("invalid", result.Body.State);
        Assert.Equal("activity-data.invalid-source-or-target", result.Body.Code);
        Assert.Contains(result.Body.Analysis!.Findings, finding => finding.Type == "ValueFlow/UnavailableProducer");
        Assert.NotNull(result.Body.SnapshotHash);
        Assert.NotNull(result.Body.ContextRevision);
    }

    [Fact]
    public async Task A_refused_answer_is_still_withheld_when_access_ends_while_it_is_analyzed()
    {
        using var host = new Host(memberReads: false);
        host.Owner.RevokeAfterFirstRead = true;

        var result = await host.Invoke(host.Request() with { Query = new() { Source = ScopeFree("missing") } });

        Assert.Equal("denied", result.Body.State);
        Assert.Null(result.Body.Analysis);
    }

    private static readonly JsonSerializerOptions Web = CreateWeb();
    private static JsonSerializerOptions CreateWeb()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedShapeState>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedValueKind>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivitySerializedPresence>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ActivityChildResultAvailability>(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static ArgumentValue ScopeFree(string producer) =>
        new(JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = "$result" }), "ActivityResult");

    private static ActivityNode Node(string id) => new(id, "test", [], []);
    private static ActivityNode Sequence(params ActivityNode[] children) => new("root", "sequence", [], [], new("elsa.sequence.structure", "1.0.0",
        JsonSerializer.SerializeToElement(new { activities = children, variables = Array.Empty<VariableDefinition>() }, Web)));
    private static WorkflowDefinitionDraft Draft() => new()
    {
        Id = "draft", WorkflowDefinitionId = "workflow", TenantId = "tenant",
        State = new([], Sequence(Node("producer"), Node("consumer")), [], [], null), LastModifiedAt = DateTimeOffset.UnixEpoch
    };
    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([
        new("sub", "author"), new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, "tenant"),
        new("permission", "read"), new("exp", "1"), new("jti", "1")
    ], "test"));

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly RouteEndpoint endpoint;
        private readonly DraftStore drafts = new(Draft());
        internal DefinitionAuthority Owner { get; } = new();

        internal Host(bool memberReads)
        {
            var collection = new ServiceCollection();
            collection.AddLogging().AddRouting().AddElsaEndpoints();
            collection.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
            new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(collection);
            new Elsa.Events.EventsFeature().ConfigureServices(collection);
            new WorkflowDesignValidationsFeature { MemberReads = memberReads }.ConfigureServices(collection);
            new WorkflowsDesignApiFeature().ConfigureServices(collection);
            collection.RemoveAll<IWorkflowDefinitionAuthorizer>();
            collection.AddSingleton<IWorkflowDefinitionAuthorizer>(Owner);
            collection.AddSingleton<IActivityAuthoringCatalog>(new CatalogAuthority());
            collection.AddSingleton<IWorkflowDefinitionDraftStore>(drafts);
            collection.AddSingleton<IActivityDefinitionLookup>(new StubActivityCatalog().Add("sequence").Add("test", [
                new("CustomerId", "CustomerId", new("String"), null, "Customer ID", null, true)
            ]));
            services = collection.BuildServiceProvider();
            endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), "design/workflows/activity-data/context", "POST");
        }

        internal ActivityDataContextRequest Request() => new("1", "workflow", "draft", WorkflowDraftView.From(drafts.Draft, []).AuthoringRevision,
            "working-document", "consumer", "CustomerId");

        internal async Task<(ActivityDataContextResponse Body, string Json)> Invoke(ActivityDataContextRequest request)
        {
            using var scope = services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = Principal() };
            context.Request.Method = "POST";
            context.Request.Path = "/design/workflows/activity-data/context";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Web)));
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
            return (JsonSerializer.Deserialize<ActivityDataContextResponse>(json, Web)!, json);
        }

        public void Dispose() => services.Dispose();
    }

    private sealed class DefinitionAuthority : IWorkflowDefinitionAuthorizer
    {
        private int reads;
        internal bool RevokeAfterFirstRead { get; set; }
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definitionId == "workflow" && !(RevokeAfterFirstRead && reads++ > 0));
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class CatalogAuthority : IActivityAuthoringCatalog
    {
        private static readonly IReadOnlyList<ActivityAuthoringCatalogEntry> Entries = new[] { "test", "sequence" }.Select(id =>
            new ActivityAuthoringCatalogEntry(id, id, "1.0.0", id, null, null, true, null, [])).ToArray();
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityAuthoringCatalogResult(true, Entries));
    }

    private sealed class DraftStore(WorkflowDefinitionDraft draft) : IWorkflowDefinitionDraftStore
    {
        internal WorkflowDefinitionDraft Draft { get; } = draft;
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(draftId == Draft.Id ? Draft : null);
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
