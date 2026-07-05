using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Pins that the <see cref="RequiredInputOutputValidator"/> derives errors against the CURRENT
/// catalog state on every pass (errors are derived, never cached against the draft), that it looks
/// each distinct <c>ActivityVersionId</c> up exactly once per pass (per-pass memoization), and that
/// a missing catalog version is FAIL-CLOSED: production <c>ActivityDefinitionLookup.GetVersion</c>
/// passes through the version store, and both store impls throw <see cref="EntityNotFoundException"/>
/// on a missing id (they never return null). So removing a node's version does not silently drop the
/// error — it faults the gate: the unshielded write gate propagates the exception, and the shielded
/// read gate folds it into the reserved <c>Validation/Faulted</c> synthetic.
/// <para>
/// Whether a missing catalog version should instead surface as a first-class validation error is a
/// separate, pending spec decision (missing-version-as-validation-error), not pinned here.
/// </para>
/// </summary>
public sealed class RequiredInputOutputValidatorDerivationTests
{
    private static InputDefinition RequiredInput(string referenceKey) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: new TypeReference("String"),
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsRequired: true);

    [Fact]
    public async Task Live_derivation_reflects_current_catalog_state_as_node_bindings_change()
    {
        // The valid catalog-mutation pin: the error set is derived against the catalog + draft on every
        // pass. Version present + required "body" unbound → error; then bind "body" → error gone. No
        // reliance on null-returns (the version stays present throughout).
        var catalog = new MutableLookup();
        catalog.Set("av-1", inputs: [RequiredInput("body")], outputs: []);
        var validator = new RequiredInputOutputValidator(catalog, Options(), Walker());

        var missing = State(activities: [Node("n1", "av-1")]);
        var before = await Validate(validator, missing);
        Assert.Single(before, e => e.Path == "n1/inputs/body");

        var bound = State(activities: [Node("n1", "av-1", inputs: [LiteralInput("body", "hello")])]);
        var after = await Validate(validator, bound);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Removing_the_version_faults_the_unshielded_write_gate()
    {
        // Removing the version makes GetVersion throw EntityNotFoundException. Through the unshielded
        // write gate (DeriveValidationErrorsAsync) that exception propagates — the caller's write fails.
        var catalog = new MutableLookup();
        catalog.Set("av-1", inputs: [RequiredInput("body")], outputs: []);

        var state = State(activities: [Node("n1", "av-1")]);
        var publisher = new ValidatingPublisher(new RequiredInputOutputValidator(catalog, Options(), Walker()));

        // Sanity: with the version present, deriving succeeds and yields the required-input error.
        var before = await publisher.DeriveValidationErrorsAsync(new StubDraft(state), CancellationToken.None);
        Assert.Single(before, e => e.Path == "n1/inputs/body");

        catalog.Remove("av-1");

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => publisher.DeriveValidationErrorsAsync(new StubDraft(state), CancellationToken.None));
    }

    [Fact]
    public async Task Removing_the_version_surfaces_as_Validation_Faulted_on_the_shielded_read_gate()
    {
        // Same missing-version fault, seen through the shielded read gate (TryDeriveValidationErrorsAsync):
        // the throw is folded into the reserved Validation/Faulted synthetic so the Draft stays readable.
        var catalog = new MutableLookup();
        catalog.Set("av-1", inputs: [RequiredInput("body")], outputs: []);
        catalog.Remove("av-1");

        var state = State(activities: [Node("n1", "av-1")]);
        var publisher = new ValidatingPublisher(new RequiredInputOutputValidator(catalog, Options(), Walker()));

        var errors = await publisher.TryDeriveValidationErrorsAsync(new StubDraft(state), CancellationToken.None);

        var faulted = Assert.Single(errors, e => e.Type == ValidationCategories.Faulted);
        Assert.Equal(ValidationPaths.Workflow, faulted.Path);
        Assert.Contains(nameof(EntityNotFoundException), faulted.Message);
    }

    [Fact]
    public async Task Catalog_lookup_is_memoized_once_per_pass_for_shared_version_ids()
    {
        // N nodes sharing one ActivityVersionId → exactly one GetVersion round-trip per pass.
        var catalog = new CountingLookup();
        catalog.Set("av-1", inputs: [RequiredInput("body")], outputs: []);

        var state = State(activities: [
            Node("n1", "av-1"),
            Node("n2", "av-1"),
            Node("n3", "av-1"),
        ]);
        var validator = new RequiredInputOutputValidator(catalog, Options(), Walker());

        await Validate(validator, state);

        Assert.Equal(1, catalog.CallCount("av-1"));
    }

    private sealed class StubDraft(Core.Models.WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public Core.Models.WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Minimal <see cref="IEventPublisher"/> that runs a single validator against
    /// <see cref="OnDraftValidating"/> and aggregates its errors — exercising the gate's publish +
    /// read-back contract with the real validator so its throwing behaviour reaches the gate.
    /// </summary>
    private sealed class ValidatingPublisher(IDraftValidator validator) : IEventPublisher
    {
        public async Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            if (@event is OnDraftValidating validating)
                foreach (var error in await validator.Validate(validating.Draft, cancellationToken))
                    validating.Errors.Add(error);
        }
    }

    /// <summary>In-memory catalog whose versions can be added and removed mid-test.</summary>
    private sealed class MutableLookup : IActivityDefinitionLookup
    {
        // Seed the synthetic $root container's version (empty, no required args) so the fail-closed
        // fake resolves it — see ValidatorTestHelpers.RootActivityVersionId.
        private readonly Dictionary<string, IActivityDefinitionVersion> _versions =
            new(StringComparer.Ordinal) { [RootActivityVersionId] = new StubVersion(RootActivityVersionId, [], []) };

        public void Set(string versionId, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs)
            => _versions[versionId] = new StubVersion(versionId, inputs, outputs);

        public void Remove(string versionId) => _versions.Remove(versionId);

        // Fail-closed, mirroring the production store contract (throws on a missing id, never null).
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
            => _versions.TryGetValue(versionId, out var version)
                ? Task.FromResult(version)
                : throw EntityNotFoundException.ForEntity(typeof(IActivityDefinitionVersion), versionId);

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    /// <summary>In-memory catalog that counts <see cref="GetVersion"/> calls per version id.</summary>
    private sealed class CountingLookup : IActivityDefinitionLookup
    {
        // Seed the synthetic $root container's version so the fail-closed fake resolves it.
        private readonly Dictionary<string, IActivityDefinitionVersion> _versions =
            new(StringComparer.Ordinal) { [RootActivityVersionId] = new StubVersion(RootActivityVersionId, [], []) };
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

        public void Set(string versionId, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs)
            => _versions[versionId] = new StubVersion(versionId, inputs, outputs);

        public int CallCount(string versionId) => _calls.TryGetValue(versionId, out var count) ? count : 0;

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
        {
            _calls[versionId] = CallCount(versionId) + 1;
            return _versions.TryGetValue(versionId, out var version)
                ? Task.FromResult(version)
                : throw EntityNotFoundException.ForEntity(typeof(IActivityDefinitionVersion), versionId);
        }

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubVersion(string id, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs) : IActivityDefinitionVersion
    {
        public string Id { get; } = id;
        public string Version => "1.0.0";
        public string DefinitionId => "def-1";
        public string ActivityTypeKey => "TestActivity";
        public string DescriptorType => "Test";
        public System.Text.Json.JsonElement DescriptorPayload => default;
        public string SourceKind => "Test";
        public string SourceId => "Test";
        public IActivityDefinition Definition => null!;
        public IEnumerable<InputDefinition> Inputs { get; } = inputs;
        public IEnumerable<OutputDefinition> Outputs { get; } = outputs;
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => default;
        public string Hash => "";
    }
}
