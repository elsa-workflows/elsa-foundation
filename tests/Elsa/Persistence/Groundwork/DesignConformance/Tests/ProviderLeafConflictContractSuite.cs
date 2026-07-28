using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Provider-neutral proof of the one-provider-per-host Groundwork invariant. A concrete leaf composes its
/// own provider in each reference-host deployment shape (runtime-only, design-only, combined) and supplies
/// a second, different Groundwork provider leaf. The shared contract asserts that adding the second leaf is
/// rejected at registration time with the established diagnostic, in either composition order. The guard
/// fires while descriptors are being added, so no provider connection is opened and no fixture is required.
/// </summary>
public abstract class ProviderLeafConflictContractSuite
{
    public enum ReferenceHostShape
    {
        RuntimeOnly,
        DesignOnly,
        Combined,
    }

    /// <summary>The concrete leaf's own selected provider identity (for example <c>sqlite</c>).</summary>
    protected abstract string PrimaryProviderIdentity { get; }

    /// <summary>A different real Groundwork provider identity composed as the conflicting leaf.</summary>
    protected abstract string ConflictingProviderIdentity { get; }

    /// <summary>Composes the primary provider in one reference-host deployment shape.</summary>
    protected abstract void ComposePrimary(ReferenceHostShape shape, IServiceCollection services);

    /// <summary>Composes a second, different Groundwork provider leaf into the same host.</summary>
    protected abstract void AddConflictingProviderLeaf(IServiceCollection services);

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void Composing_a_second_provider_leaf_is_rejected_in_the_reference_host(ReferenceHostShape shape)
    {
        var services = new ServiceCollection();
        ComposePrimary(shape, services);

        var conflict = Assert.Throws<InvalidOperationException>(() => AddConflictingProviderLeaf(services));

        AssertEstablishedDiagnostic(conflict);
    }

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void Second_provider_leaf_rejection_is_order_independent(ReferenceHostShape shape)
    {
        var services = new ServiceCollection();
        AddConflictingProviderLeaf(services);

        var conflict = Assert.Throws<InvalidOperationException>(() => ComposePrimary(shape, services));

        AssertEstablishedDiagnostic(conflict);
    }

    private void AssertEstablishedDiagnostic(InvalidOperationException exception)
    {
        var sortedPair = string.Join(
            ", ",
            new[] { PrimaryProviderIdentity, ConflictingProviderIdentity }
                .OrderBy(identity => identity, StringComparer.Ordinal)
                .Select(identity => $"'{identity}'"));

        Assert.Contains(
            "Conflicting Groundwork provider leaves were selected:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(sortedPair, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Select exactly one concrete Groundwork provider leaf per host.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
