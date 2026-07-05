using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Pins that the <see cref="RequiredInputOutputValidator"/> derives errors against the CURRENT
/// catalog state on every pass (errors are derived, never cached against the draft), and that it
/// looks each distinct <c>ActivityVersionId</c> up exactly once per pass (the per-pass memoization
/// added in the review fix).
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
    public async Task Live_derivation_reflects_current_catalog_state_when_the_version_is_removed()
    {
        // This pins the CURRENT behaviour: because the error set is derived against the catalog on
        // every pass, removing a node's version from the catalog makes the validator continue-on-
        // missing-version (Unknown_activity_version_is_skipped_gracefully), so the previously-emitted
        // required-input error DISAPPEARS on the next pass over the UNCHANGED draft.
        //
        // Whether a missing catalog version should itself be a validation error is deliberately NOT
        // asserted here — it is tracked as a separate spec decision (missing-version-as-error), not
        // pinned by this test.
        var catalog = new MutableLookup();
        catalog.Set("av-1", inputs: [RequiredInput("body")], outputs: []);

        // Draft node declares av-1 with the required "body" input unbound → error present.
        var state = State(activities: [Node("n1", "av-1")]);
        var validator = new RequiredInputOutputValidator(catalog, Options(), Walker());

        var before = await ValidateOnce(validator, state);
        Assert.Single(before, e => e.Path == "n1/inputs/body");

        // Remove the version from the catalog; the draft is unchanged.
        catalog.Remove("av-1");

        var after = await ValidateOnce(validator, state);
        Assert.Empty(after);
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

        await ValidateOnce(validator, state);

        Assert.Equal(1, catalog.CallCount("av-1"));
    }

    private static async Task<IReadOnlyList<ValidationError>> ValidateOnce(
        RequiredInputOutputValidator validator,
        Core.Models.WorkflowDefinitionState state) =>
        [.. await validator.Validate(new StubDraftForDerivation(state), CancellationToken.None)];

    private sealed class StubDraftForDerivation(Core.Models.WorkflowDefinitionState state) : Core.Contracts.IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public Core.Models.WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }

    /// <summary>In-memory catalog whose versions can be added and removed mid-test.</summary>
    private sealed class MutableLookup : IActivityDefinitionLookup
    {
        private readonly Dictionary<string, IActivityDefinitionVersion> _versions = new(StringComparer.Ordinal);

        public void Set(string versionId, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs)
            => _versions[versionId] = new StubVersion(versionId, inputs, outputs);

        public void Remove(string versionId) => _versions.Remove(versionId);

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_versions.TryGetValue(versionId, out var version) ? version : null!);

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
        private readonly Dictionary<string, IActivityDefinitionVersion> _versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

        public void Set(string versionId, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs)
            => _versions[versionId] = new StubVersion(versionId, inputs, outputs);

        public int CallCount(string versionId) => _calls.TryGetValue(versionId, out var count) ? count : 0;

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
        {
            _calls[versionId] = CallCount(versionId) + 1;
            return Task.FromResult(_versions.TryGetValue(versionId, out var version) ? version : null!);
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
