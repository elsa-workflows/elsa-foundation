using Elsa.Api.AspNetCore;
using Elsa.Architecture.Tests.Support;
using NativeEndpoints;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(OpenApiLifetimeCollectibilityCollection.Name)]
public sealed class OpenApiLifetimeCollectibilityTests
{
    [Fact]
    public void Collectible_contract_metadata_is_retained_by_real_openapi_generation()
    {
        var cycle = OpenApiLifetimeFixture.Create(OpenApiContractLifetime.Collectible);

        var evidence = cycle.VerifyCollection();

        Assert.False(evidence.Collected, "The unsafe control must reproduce framework retention.");
        Assert.True(evidence.ContractTypeAlive, evidence.Diagnostic);
        Assert.True(evidence.LoadContextAlive, evidence.Diagnostic);
        Assert.False(evidence.DelegateAlive, evidence.Diagnostic);
        Assert.False(evidence.ProviderAlive, evidence.Diagnostic);
    }

    [Fact]
    public void Stable_contract_metadata_releases_the_collectible_implementation()
    {
        for (var cycleNumber = 0; cycleNumber < 3; cycleNumber++)
        {
            var cycle = OpenApiLifetimeFixture.Create(OpenApiContractLifetime.Stable);

            var evidence = cycle.VerifyCollection();

            Assert.True(evidence.SpecificSchemas, evidence.Diagnostic);
            Assert.True(evidence.Collected, evidence.Diagnostic);
        }
    }

    [Fact]
    public void Rejected_candidate_never_replaces_the_previous_callable_documented_generation()
    {
        var evidence = OpenApiLifetimeFixture.RejectUnsafeCandidate();

        Assert.True(evidence.PreviousDocumentedBefore);
        Assert.True(evidence.PreviousDocumentedAfter);
        Assert.True(evidence.CandidateNeverDocumented);
        Assert.True(evidence.PreviousCallableAfter);
        Assert.Equal(EndpointLifetimeViolationCategory.RequestType, evidence.Violation.Category);
        Assert.Equal("Elsa.OpenApi.LifetimeFixture", evidence.Violation.Group);
    }

    [Fact]
    public void Accepted_replacement_documents_one_complete_generation_before_and_after_the_swap()
    {
        var evidence = OpenApiLifetimeFixture.ReplaceAcceptedGeneration();

        Assert.True(evidence.PreviousCompleteBefore);
        Assert.True(evidence.CandidateAbsentBefore);
        Assert.True(evidence.PreviousAbsentAfter);
        Assert.True(evidence.CandidateCompleteAfter);
        Assert.True(evidence.CandidateCallableAfter);
        Assert.True(evidence.ConcurrentDocumentsComplete);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpenApiLifetimeCollectibilityCollection
{
    public const string Name = "OpenAPI lifetime collectibility";
}
