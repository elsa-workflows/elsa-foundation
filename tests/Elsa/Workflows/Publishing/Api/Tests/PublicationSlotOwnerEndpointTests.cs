using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// #1659 over HTTP: both workflow preflight routes report the owner of the target activation slot when it is not
/// publishing, folding it into <c>canActivate</c> while <c>conflicts</c> stays trigger-only, and publish refuses such a
/// slot before it writes anything.
/// </summary>
/// <remarks>
/// The failure that looks like success is a green preflight for a slot publish can never take: a slot live under an
/// imported artifact answered <c>canActivate: true</c>, and the publish that followed wrote an executable, a source
/// reference and a failed publication record before activation refused it. The publishable-slot theory pins the other
/// direction, so an owner check that over-reports cannot pass either.
/// </remarks>
public sealed class PublicationSlotOwnerEndpointTests : IAsyncLifetime
{
    private const string VersionId = "version-route";
    private const string VersionPreflightRoute = $"/publishing/workflows/{VersionId}/preflight";
    private const string SnapshotPreflightRoute = "/publishing/workflows/preflight";
    private const string PublishRoute = $"/publishing/workflows/{VersionId}/publish";

    // The scenario host's capture compiler answers every compile with this definition, so every route resolves its slot.
    private const string DefinitionId = "capture-definition";
    private const string StimulusType = "Http";
    private const string StimulusHash = "get:/orders";
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource ImportOwner = WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts");

    private readonly StubTriggerExtractor _triggers = new();
    private PublishingMinimalApiScenarioHost _host = null!;
    private IWorkflowActivationAuthority _authority = null!;

    public enum SlotState
    {
        Empty,
        Publishing,
        Vacated
    }

    public async Task InitializeAsync()
    {
        _host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            configureServices: services =>
            {
                services.RemoveAll<IWorkflowTriggerBindingExtractor>();
                services.AddSingleton<IWorkflowTriggerBindingExtractor>(_triggers);
                // Normally contributed by the runtime triggers feature, which this host does not mount. Without it the
                // coordinator refuses to run at all, so a publish that got past the gate could never reach the authority.
                services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
                services.AddSingleton<IWorkflowDefinitionVersionStore>(new FakeVersionStore(
                    new WorkflowDefinitionVersion(DefinitionId, "1.0.0")
                    {
                        Id = VersionId,
                        Definition = new WorkflowDefinition { Id = DefinitionId, Name = "Capture" },
                        State = WorkflowDefinitionState.Empty
                    }));
                services.AddSingleton<IExpressionDraftSemanticValidator, ValidExpressions>();
            });
        _authority = _host.Services.GetRequiredService<IWorkflowActivationAuthority>();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(VersionPreflightRoute)]
    [InlineData(SnapshotPreflightRoute)]
    public async Task A_slot_owned_by_an_imported_artifact_is_reported_and_blocks_activation(string route)
    {
        await ActivateAsync("default", "import:artifact-1", ImportOwner);

        var preflight = await PreflightAsync(route);

        Assert.False(preflight.GetProperty("canActivate").GetBoolean());
        Assert.Equal(0, preflight.GetProperty("conflicts").GetArrayLength());
        AssertImportOwner(preflight);
    }

    [Theory]
    [InlineData(VersionPreflightRoute, SlotState.Empty)]
    [InlineData(VersionPreflightRoute, SlotState.Publishing)]
    [InlineData(VersionPreflightRoute, SlotState.Vacated)]
    [InlineData(SnapshotPreflightRoute, SlotState.Empty)]
    [InlineData(SnapshotPreflightRoute, SlotState.Publishing)]
    [InlineData(SnapshotPreflightRoute, SlotState.Vacated)]
    public async Task A_slot_publishing_may_take_reports_no_owner_and_keeps_its_verdict(string route, SlotState state)
    {
        await ArrangeDefaultSlotAsync(state);

        var preflight = await PreflightAsync(route);

        Assert.True(preflight.GetProperty("canActivate").GetBoolean());
        // Present and explicitly null, so a client can tell "no owner" apart from a server that predates the field.
        Assert.Equal(JsonValueKind.Null, preflight.GetProperty("targetSlotOwner").ValueKind);
    }

    [Theory]
    [InlineData(VersionPreflightRoute, true)]
    [InlineData(VersionPreflightRoute, false)]
    [InlineData(SnapshotPreflightRoute, true)]
    [InlineData(SnapshotPreflightRoute, false)]
    public async Task A_trigger_conflict_is_reported_beside_the_owner_without_being_mistaken_for_one(string route, bool foreignOwned)
    {
        if (foreignOwned)
            await ActivateAsync("default", "import:artifact-1", ImportOwner);
        await ClaimExclusiveTriggerInAnotherSlotAsync("blue", "publication-blue");

        var preflight = await PreflightAsync(route);

        Assert.False(preflight.GetProperty("canActivate").GetBoolean());
        var conflict = Assert.Single(preflight.GetProperty("conflicts").EnumerateArray().ToArray());
        Assert.Equal("publication-blue", conflict.GetProperty("publicationId").GetString());
        Assert.Equal("blue", conflict.GetProperty("slotName").GetString());
        if (foreignOwned)
            AssertImportOwner(preflight);
        else
            Assert.Equal(JsonValueKind.Null, preflight.GetProperty("targetSlotOwner").ValueKind);
    }

    [Fact]
    public async Task Publish_to_a_slot_owned_by_an_imported_artifact_keeps_its_409_but_writes_nothing()
    {
        await ActivateAsync("default", "import:artifact-1", ImportOwner);

        using var response = await PostAsync(PublishRoute, "{}");
        var raw = await response.Content.ReadAsStringAsync();

        // The conflict problem a refused activation already produced, now reached before the first write.
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(raw);
        Assert.Contains(ImportOwner.Describe(), problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Empty(await _host.Services.GetRequiredService<IWorkflowExecutableStore>().ListAllAsync());
        Assert.Empty(await _host.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().ListAllAsync());
        Assert.Empty(await _host.Services.GetRequiredService<IPublicationRecordStore>()
            .ListBySlotAsync(WorkflowActivationSlotIdentity.Create(DefinitionId, "default")));
    }

    private static void AssertImportOwner(JsonElement preflight)
    {
        var owner = preflight.GetProperty("targetSlotOwner");
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, owner.GetProperty("sourceKind").GetString());
        Assert.Equal(ImportOwner.SourceId, owner.GetProperty("sourceId").GetString());
    }

    private async Task ArrangeDefaultSlotAsync(SlotState state)
    {
        switch (state)
        {
            case SlotState.Publishing:
                await ActivateAsync("default", "publication-current", WorkflowActivationSource.Publishing);
                break;
            case SlotState.Vacated:
                // Withdrawn by its own foreign owner: deactivation clears the source along with the activation.
                var imported = await ActivateAsync("default", "import:artifact-1", ImportOwner);
                var vacated = await _authority.TryDeactivateAsync(DefinitionId, "default", ImportOwner, imported.Revision, Now);
                Assert.True(vacated.Succeeded, vacated.Diagnostic);
                Assert.Null(vacated.Slot.Source);
                break;
        }
    }

    private async Task<WorkflowActivationSlot> ActivateAsync(string slotName, string activationId, WorkflowActivationSource source)
    {
        var current = await _authority.FindAsync(DefinitionId, slotName);
        var transition = await _authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            DefinitionId, slotName, activationId, source, current?.Revision ?? 0, Now));
        Assert.True(transition.Succeeded, transition.Diagnostic);
        return transition.Slot;
    }

    /// <summary>Makes another live slot hold an Exclusive claim that the candidate then claims too.</summary>
    private async Task ClaimExclusiveTriggerInAnotherSlotAsync(string slotName, string activationId)
    {
        await ActivateAsync(slotName, activationId, WorkflowActivationSource.Publishing);
        await _host.Services.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(
            ExclusiveBinding($"artifact-{slotName}", $"{slotName}-node") with
            {
                ActivationId = activationId,
                SlotId = WorkflowActivationSlotIdentity.Create(DefinitionId, slotName)
            });
        _triggers.ClaimsExclusiveTrigger = true;
    }

    private async Task<JsonElement> PreflightAsync(string route)
    {
        using var response = await PostAsync(route, route == SnapshotPreflightRoute
            ? $$"""{"definitionId":"{{DefinitionId}}","state":{},"layout":[]}"""
            : "{}");
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {raw}");
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> PostAsync(string route, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await _host.Client.SendAsync(request);
    }

    private static WorkflowTriggerBinding ExclusiveBinding(string artifactId, string executableNodeId) =>
        new(
            WorkflowTriggerBinding.BuildId(artifactId, executableNodeId, StimulusHash),
            artifactId,
            DefinitionId,
            "1.0",
            "capture-hash",
            executableNodeId,
            StimulusType,
            StimulusHash,
            CorrelationScope: null,
            new Dictionary<string, string>(),
            Now,
            Cardinality: TriggerCardinality.Exclusive);

    /// <summary>Claims one Exclusive stimulus on the candidate's root node once a test asks for a trigger clash.</summary>
    private sealed class StubTriggerExtractor : IWorkflowTriggerBindingExtractor
    {
        public bool ClaimsExclusiveTrigger { get; set; }

        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) =>
            ClaimsExclusiveTrigger
                ? [ExclusiveBinding(executable.Identity.ArtifactId, executable.RootActivity.ExecutableNodeId)]
                : [];
    }

    /// <summary>Hands the publish request to the real handler, as the mediator would; the capture sender would answer it.</summary>
    private sealed class PublishHandlerSender(IHttpContextAccessor accessor) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            (T)(object)await accessor.HttpContext!.RequestServices
                .GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>()
                .Handle((PublishWorkflow)(object)request, cancellationToken);
    }

    private sealed class ValidExpressions : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ExpressionDraftValidationResult(ExpressionDraftValidationState.Valid, []));
    }
}
