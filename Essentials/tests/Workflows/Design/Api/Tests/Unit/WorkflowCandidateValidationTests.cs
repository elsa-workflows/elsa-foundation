using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;
using Endpoint = Elsa.Workflows.Design.Api.Endpoints.Drafts.ValidateCandidate.Endpoint;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowCandidateValidationTests
{
    [Fact]
    public async Task Uses_normal_validation_on_a_detached_candidate_without_changing_the_saved_draft()
    {
        var host = new Host();
        var saved = host.Store.Draft.State;
        host.Publisher.Validate = validating =>
        {
            Assert.NotSame(host.Store.Draft, validating.Draft);
            Assert.NotSame(saved, validating.Draft.State);
            Assert.Equal(host.Store.Draft.Id, validating.Draft.Id);
            Assert.Equal(host.Store.Draft.WorkflowDefinitionId, validating.Draft.WorkflowDefinitionId);
        };
        var result = await host.Invoke();
        Assert.Equal("validated", result.State);
        Assert.Empty(result.Errors);
        Assert.Same(saved, host.Store.Draft.State);
        Assert.Equal(1, host.Publisher.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denies_read_only_or_source_owned_resources_before_running_validators(bool sourceOwned)
    {
        var host = new Host();
        host.Definitions.Definition.IsSourceOwned = sourceOwned;
        host.Authority.Allowed = sourceOwned;
        Assert.Equal("denied", (await host.Invoke()).State);
        Assert.Equal(0, host.Publisher.Calls);
    }

    [Fact]
    public async Task An_unacknowledged_baseline_cannot_validate_an_unsaved_proposal()
    {
        var host = new Host();
        var request = host.Request() with { Baseline = host.Request().Baseline with { AuthoringRevision = "obsolete" } };
        Assert.Equal("stale", (await host.Endpoint.HandleAsync(request, default)).State);
        Assert.Equal(0, host.Publisher.Calls);
    }

    [Theory]
    [InlineData("authority", "denied")]
    [InlineData("source", "denied")]
    [InlineData("catalog", "stale")]
    [InlineData("layout", "stale")]
    [InlineData("presentation", "stale")]
    public async Task Refuses_changes_during_validation_without_disclosing_the_stale_findings(string change, string state)
    {
        var host = new Host();
        host.Publisher.Validate = validating =>
        {
            validating.Errors.Add(new("node/input", "PrivateFinding", "Private candidate detail"));
            switch (change)
            {
                case "authority": host.Authority.Allowed = false; break;
                case "source": host.Definitions.Definition.IsSourceOwned = true; break;
                case "catalog": host.Catalog.Entries = [new("another", "another", "1", "Another", null, null, true, null, [])]; break;
                case "layout": host.Store.Layout = [new("node", 5, 6)]; break;
                case "presentation": host.Store.Presentation = [new("node", "Changed")]; break;
            }
        };
        var result = await host.Invoke();
        Assert.Equal(state, result.State);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Error", "invalid")]
    [InlineData("Warning", "validated")]
    public async Task Retains_normal_error_and_warning_severity(string severity, string state)
    {
        var host = new Host();
        host.Publisher.Validate = validating => validating.Errors.Add(new ValidationError("node/inputs/x", "NormalGate", "Normal finding") { Severity = severity });
        var result = await host.Invoke();
        Assert.Equal(state, result.State);
        Assert.Equal(severity, Assert.Single(result.Errors).Severity);
    }

    [Fact]
    public async Task A_throwing_validator_fails_closed_without_changing_the_resource()
    {
        var host = new Host();
        var before = host.Store.Draft.State;
        host.Publisher.Validate = _ => throw new InvalidOperationException("validator fault");
        await Assert.ThrowsAsync<InvalidOperationException>(host.Invoke);
        Assert.Same(before, host.Store.Draft.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_authority_fails_before_resource_reads(int count)
    {
        var host = new Host();
        var endpoint = host.Create(Enumerable.Repeat<IWorkflowDefinitionAuthorizer>(host.Authority, count));
        Assert.Equal("unavailable", (await endpoint.HandleAsync(host.Request(), default)).State);
        Assert.Equal(0, host.Store.Reads);
    }

    private sealed class Host
    {
        internal readonly DraftStore Store = new();
        internal readonly DefinitionStore Definitions = new();
        internal readonly Authority Authority = new();
        internal readonly Catalog Catalog = new();
        internal readonly Publisher Publisher = new();
        internal Endpoint Endpoint => Create([Authority]);
        // The no-root proposal deliberately needs no structure or version lookup: these guards must
        // precede metadata access even for an empty body. Real graph admission is covered by the owner suites.
        internal Endpoint Create(IEnumerable<IWorkflowDefinitionAuthorizer> owners) => new(Store, Definitions, owners, [Catalog], null!, null!, new Credentials(), Publisher);
        internal ValidateWorkflowCandidate Request() => new("draft", WorkflowDraftView.From(Store.Draft, Store.Layout, Store.Presentation), new(WorkflowDraftView.From(Store.Draft, [], []).State, [], []));
        internal Task<WorkflowCandidateValidationView> Invoke() => Endpoint.HandleAsync(Request(), default);
    }
    private sealed class Authority : IWorkflowDefinitionAuthorizer
    {
        internal bool Allowed = true;
        public ValueTask<bool> CanReadAsync(string id, CancellationToken ct = default) => ValueTask.FromResult(Allowed);
        public ValueTask<bool> CanManageAsync(string id, CancellationToken ct = default) => ValueTask.FromResult(Allowed);
    }
    private sealed class Catalog : IActivityAuthoringCatalog
    {
        internal IReadOnlyList<ActivityAuthoringCatalogEntry> Entries = [];
        public ValueTask<bool> CanReadAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken ct = default) => ValueTask.FromResult(new ActivityAuthoringCatalogResult(true, Entries));
    }
    private sealed class Credentials : ICredentialLiteralValidator
    {
        public ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<ValidationError>>([]);
    }
    private sealed class Publisher : IInlineEventPublisher
    {
        internal int Calls;
        internal Action<DraftValidating> Validate = _ => { };
        public Task Publish(IEvent value, CancellationToken ct = default) { Calls++; Validate(Assert.IsType<DraftValidating>(value)); return Task.CompletedTask; }
    }
    private sealed class DraftStore : IWorkflowDefinitionDraftStore
    {
        internal readonly WorkflowDefinitionDraft Draft = new() { Id = "draft", WorkflowDefinitionId = "definition", TenantId = "tenant", State = WorkflowDefinitionState.Empty };
        internal IReadOnlyCollection<DesignMetadataRecord> Layout = [];
        internal IReadOnlyCollection<ActivityPresentationRecord> Presentation = [];
        internal int Reads;
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string id, CancellationToken ct = default) { Reads++; return Task.FromResult<DraftWithLayout?>(new(Draft, Layout, Presentation)); }
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class DefinitionStore : IWorkflowDefinitionStore
    {
        internal readonly WorkflowDefinition Definition = new() { Id = "definition", TenantId = "tenant" };
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<WorkflowDefinition?>(Definition);
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
