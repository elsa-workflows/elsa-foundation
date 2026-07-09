using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Exceptions;
using Elsa.Workflows.Runtime.Http.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// Publish-time (template, method) uniqueness on the indexer's PRE-write validation seam (issue #592 item 2).
/// The validator inspects the about-to-be-written binding set against the existing store; a throw here fails
/// the publish before anything is deleted or saved, so the durable index stays clean.
/// </summary>
public sealed class HttpEndpointRoutingUniquenessValidatorTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();

    private HttpEndpointRoutingUniquenessValidator Validator() => new(_store);

    private static WorkflowTriggerIndexSnapshot Snapshot(params WorkflowTriggerBinding[] bindings) =>
        new(bindings[0].ArtifactId, bindings);

    [Fact]
    public async Task Throws_WhenAnotherDefinitionAlreadyClaimsTheTemplateAndMethod()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        var incoming = Snapshot(Bindings.HttpEndpoint("a2", "n2", "orders/{id}", "GET", definitionId: "def-b"));

        var exception = await Assert.ThrowsAsync<EndpointRoutingConflictException>(() => Validator().ValidateAsync(incoming).AsTask());
        Assert.Equal("GET orders/{id}", exception.Endpoint);
    }

    [Fact]
    public async Task IgnoresExistingBindings_OfTheArtifactBeingRepublished()
    {
        // Republish: the artifact's own prior bindings are about to be superseded by delete-and-resave, so they
        // must never count as a conflict against the artifact's new set.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        var republish = Snapshot(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        await Validator().ValidateAsync(republish); // must not throw
    }

    [Fact]
    public async Task AllowsSameDefinition_AcrossArtifacts()
    {
        // A new artifact (version) of the SAME definition claiming the same (template, method) is versioning,
        // not a cross-definition conflict.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        var newVersion = Snapshot(Bindings.HttpEndpoint("a2", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        await Validator().ValidateAsync(newVersion); // must not throw
    }

    [Fact]
    public async Task AllowsDifferentMethods_OnTheSameTemplate_AcrossDefinitions()
    {
        // GET vs POST orders/{id} are distinct (template, method) identities — different hashes — so two
        // definitions may each own one.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));

        var incoming = Snapshot(Bindings.HttpEndpoint("a2", "n2", "orders/{id}", "POST", definitionId: "def-b"));

        await Validator().ValidateAsync(incoming); // must not throw
    }

    [Fact]
    public async Task IgnoresNonHttpBindings_SharedStimulusIdentityIsLegitimateFanOutElsewhere()
    {
        // Two definitions sharing e.g. a Timer stimulus hash is legitimate fan-out — the validator must not
        // enforce uniqueness on any stimulus type but HTTP's.
        var existingTimer = Bindings.Other("a1", "n1", "Timer");
        await _store.SaveAsync(existingTimer);

        // Same stimulus (type, hash) as the stored Timer binding, different artifact and definition.
        var incoming = new WorkflowTriggerIndexSnapshot("a2", [existingTimer with
        {
            TriggerBindingId = WorkflowTriggerBinding.BuildId("a2", "n2", existingTimer.StimulusHash),
            ArtifactId = "a2",
            DefinitionId = "def-b",
            ExecutableNodeId = "n2",
        }]);

        await Validator().ValidateAsync(incoming); // must not throw
    }
}
