using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationPolicyTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly PublicationPolicyResolver _resolver = new();

    [Fact]
    public void ExplicitRequestOverridesWorkflowAndHostPolicies()
    {
        var result = _resolver.Resolve(
            "definition-1",
            "version-1",
            new PublicationRequestIntent(PublicationAction.PublishSideBySide, "blue"),
            Policy("definition-1", PublicationPolicyDefaultAction.ReplaceDefaultSlot, "workflow", 7),
            Policy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "host", 3));

        Assert.Equal(PublicationAction.PublishSideBySide, result.Action);
        Assert.Equal("blue", result.SlotName);
        Assert.Equal(PublicationPolicySource.Request, result.PolicySource);
        Assert.Null(result.PolicyRevision);
    }

    [Fact]
    public void WorkflowPolicyOverridesHostPolicy()
    {
        var result = _resolver.Resolve(
            "definition-1",
            "version-1",
            request: null,
            Policy("definition-1", PublicationPolicyDefaultAction.ReplaceDefaultSlot, "production", 7),
            Policy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 3));

        Assert.Equal(PublicationAction.Replace, result.Action);
        Assert.Equal("production", result.SlotName);
        Assert.Equal(PublicationPolicySource.Workflow, result.PolicySource);
        Assert.Equal(7, result.PolicyRevision);
    }

    [Fact]
    public void HostPolicyIsUsedWhenNoHigherPrecedenceIntentExists()
    {
        var result = _resolver.Resolve(
            "definition-1",
            "version-1",
            request: null,
            workflowPolicy: null,
            Policy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 3));

        Assert.Equal(PublicationAction.Replace, result.Action);
        Assert.Equal("default", result.SlotName);
        Assert.Equal(PublicationPolicySource.Host, result.PolicySource);
        Assert.Equal(3, result.PolicyRevision);
    }

    [Fact]
    public void RequireExplicitSlotDoesNotSilentlyCreateSideBySidePublication()
    {
        var exception = Assert.Throws<PublicationPolicyResolutionException>(() => _resolver.Resolve(
            "definition-1",
            "version-1",
            request: null,
            Policy("definition-1", PublicationPolicyDefaultAction.RequireExplicitSlot, "default", 7),
            Policy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 3)));

        Assert.Equal("explicit_slot_required", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("default")]
    public void SideBySideRequestRequiresMeaningfulNamedSlot(string slotName)
    {
        var exception = Assert.Throws<PublicationPolicyResolutionException>(() => _resolver.Resolve(
            "definition-1",
            "version-1",
            new PublicationRequestIntent(PublicationAction.PublishSideBySide, slotName),
            workflowPolicy: null,
            Policy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 3)));

        Assert.Equal("named_slot_required", exception.Code);
    }

    private PublicationPolicy Policy(
        string? workflowDefinitionId,
        PublicationPolicyDefaultAction defaultAction,
        string defaultSlotName,
        long revision) =>
        new(workflowDefinitionId, defaultAction, defaultSlotName, revision, _now);
}
