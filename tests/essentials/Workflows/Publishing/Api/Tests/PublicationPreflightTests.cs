using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationPreflightTests
{
    private readonly PublicationPreflightService _service = new();

    [Fact]
    public void PreflightReportsAddedRemovedAndRetainedClaimsAgainstReplacedPublication()
    {
        var candidateClaims = new[]
        {
            Claim("candidate", "node-orders", "Event", "orders", PublicationTriggerCardinality.FanOut),
            Claim("candidate", "node-invoices", "Event", "invoices", PublicationTriggerCardinality.FanOut)
        };
        var currentClaims = ClaimSet(
            "publication-current",
            "default",
            isReplacedPublication: true,
            Claim("publication-current", "node-orders", "Event", "orders", PublicationTriggerCardinality.FanOut),
            Claim("publication-current", "node-customers", "Event", "customers", PublicationTriggerCardinality.FanOut));

        var result = _service.Evaluate(candidateClaims, [currentClaims]);

        Assert.True(result.CanActivate);
        Assert.Empty(result.Conflicts);
        Assert.Collection(
            result.Changes.OrderBy(change => change.StimulusHash, StringComparer.Ordinal),
            change =>
            {
                Assert.Equal("customers", change.StimulusHash);
                Assert.Equal(PublicationTriggerChangeKind.Removed, change.Change);
            },
            change =>
            {
                Assert.Equal("invoices", change.StimulusHash);
                Assert.Equal(PublicationTriggerChangeKind.Added, change.Change);
            },
            change =>
            {
                Assert.Equal("orders", change.StimulusHash);
                Assert.Equal(PublicationTriggerChangeKind.Retained, change.Change);
            });
    }

    [Fact]
    public void ExclusiveClaimConflictsWithAnotherAuthoritativeSlot()
    {
        var candidateClaims = new[]
        {
            Claim("candidate", "node-http", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive)
        };
        var otherSlot = ClaimSet(
            "publication-blue",
            "blue",
            isReplacedPublication: false,
            Claim("publication-blue", "node-http", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive));

        var result = _service.Evaluate(candidateClaims, [otherSlot]);

        Assert.False(result.CanActivate);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("publication-blue", conflict.PublicationId);
        Assert.Equal("blue", conflict.SlotName);
        Assert.Equal("get:/orders", conflict.StimulusHash);
        Assert.Equal(PublicationTriggerCardinality.Exclusive, conflict.Cardinality);
    }

    [Fact]
    public void ExclusiveClaimMayReplaceSameClaimInItsOwnSlot()
    {
        var candidateClaims = new[]
        {
            Claim("candidate", "node-http-new", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive)
        };
        var replacedSlot = ClaimSet(
            "publication-current",
            "default",
            isReplacedPublication: true,
            Claim("publication-current", "node-http-old", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive));

        var result = _service.Evaluate(candidateClaims, [replacedSlot]);

        Assert.True(result.CanActivate);
        Assert.Empty(result.Conflicts);
        var retained = Assert.Single(result.Changes);
        Assert.Equal(PublicationTriggerChangeKind.Retained, retained.Change);
    }

    [Fact]
    public void FanOutClaimsMayCoexistAcrossAuthoritativeSlots()
    {
        var candidateClaims = new[]
        {
            Claim("candidate", "node-event", "Event", "orders", PublicationTriggerCardinality.FanOut)
        };
        var otherSlot = ClaimSet(
            "publication-blue",
            "blue",
            isReplacedPublication: false,
            Claim("publication-blue", "node-event", "Event", "orders", PublicationTriggerCardinality.FanOut));

        var result = _service.Evaluate(candidateClaims, [otherSlot]);

        Assert.True(result.CanActivate);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void MixedCardinalityFailsClosedWhenEitherClaimIsExclusive()
    {
        var candidateClaims = new[]
        {
            Claim("candidate", "node-event", "Event", "orders", PublicationTriggerCardinality.Exclusive)
        };
        var otherSlot = ClaimSet(
            "publication-blue",
            "blue",
            isReplacedPublication: false,
            Claim("publication-blue", "node-event", "Event", "orders", PublicationTriggerCardinality.FanOut));

        var result = _service.Evaluate(candidateClaims, [otherSlot]);

        Assert.False(result.CanActivate);
        Assert.Single(result.Conflicts);
    }

    private static PublicationAuthoritativeClaimSet ClaimSet(
        string publicationId,
        string slotName,
        bool isReplacedPublication,
        params PublicationTriggerClaim[] claims) =>
        new(publicationId, slotName, isReplacedPublication, claims);

    private static PublicationTriggerClaim Claim(
        string publicationId,
        string executableNodeId,
        string stimulusType,
        string stimulusHash,
        PublicationTriggerCardinality cardinality) =>
        new(
            ClaimId: $"{publicationId}:{executableNodeId}:{stimulusType}:{stimulusHash}",
            PublicationId: publicationId,
            ArtifactId: $"artifact-{publicationId}",
            ExecutableNodeId: executableNodeId,
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            Cardinality: cardinality,
            Metadata: new Dictionary<string, string>());
}
