using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;

/// <summary>
/// Provider-neutral proof of the Groundwork target-declaration contract. A concrete leaf composes its own
/// provider in each reference-host deployment shape (runtime-only, design-only, combined) and then composes a
/// second, different Groundwork store. The shared contract asserts that the second store is rejected when it
/// claims an already-declared target name, accepted when it declares its own, and ignored when it re-declares
/// the identical store.
/// <para>
/// This supersedes the former one-provider-per-host invariant. Two providers in one host is now a supported
/// topology; what must never happen is a second connection silently replacing or being discarded under a name
/// that already means something else.
/// </para>
/// <para>
/// The guard fires while descriptors are being added, so no provider connection is opened and no fixture is
/// required. Whether two declared targets actually admit and serve independent schemas is proven by the
/// per-target admission tests, not here.
/// </para>
/// </summary>
public abstract class GroundworkTargetConflictContractSuite
{
    public enum ReferenceHostShape
    {
        RuntimeOnly,
        DesignOnly,
        Combined,
    }

    /// <summary>The concrete leaf's own selected provider identity (for example <c>sqlite</c>).</summary>
    protected abstract string PrimaryProviderIdentity { get; }

    /// <summary>A different real Groundwork provider identity composed as the second store.</summary>
    protected abstract string SecondProviderIdentity { get; }

    /// <summary>Composes the primary provider in one reference-host deployment shape, on the default target.</summary>
    protected abstract void ComposePrimary(ReferenceHostShape shape, IServiceCollection services);

    /// <summary>Composes a second, different Groundwork store under <paramref name="targetName"/>.</summary>
    protected abstract void AddSecondStore(IServiceCollection services, string? targetName);

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void A_different_store_claiming_an_already_declared_target_is_rejected(ReferenceHostShape shape)
    {
        var services = new ServiceCollection();
        ComposePrimary(shape, services);

        var conflict = Assert.Throws<GroundworkTargetConflictException>(
            () => AddSecondStore(services, GroundworkTargetNames.Default));

        AssertEstablishedDiagnostic(conflict);
    }

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void Same_target_rejection_is_order_independent(ReferenceHostShape shape)
    {
        var services = new ServiceCollection();
        AddSecondStore(services, GroundworkTargetNames.Default);

        var conflict = Assert.Throws<GroundworkTargetConflictException>(() => ComposePrimary(shape, services));

        AssertEstablishedDiagnostic(conflict);
    }

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void A_second_provider_declaring_its_own_target_is_accepted(ReferenceHostShape shape)
    {
        const string secondTarget = "authoring";
        var services = new ServiceCollection();
        ComposePrimary(shape, services);

        AddSecondStore(services, secondTarget);

        var registry = services.GroundworkTargets();
        Assert.Equal([secondTarget, GroundworkTargetNames.Default], registry.TargetNames);
        Assert.Equal(PrimaryProviderIdentity, registry.Require(GroundworkTargetNames.Default).ProviderIdentity);
        Assert.Equal(SecondProviderIdentity, registry.Require(secondTarget).ProviderIdentity);
    }

    [Theory]
    [InlineData(ReferenceHostShape.RuntimeOnly)]
    [InlineData(ReferenceHostShape.DesignOnly)]
    [InlineData(ReferenceHostShape.Combined)]
    public void Re_declaring_the_identical_store_under_the_same_target_is_idempotent(ReferenceHostShape shape)
    {
        var services = new ServiceCollection();
        ComposePrimary(shape, services);

        ComposePrimary(shape, services);

        var registry = services.GroundworkTargets();
        Assert.Equal([GroundworkTargetNames.Default], registry.TargetNames);
    }

    private void AssertEstablishedDiagnostic(GroundworkTargetConflictException exception)
    {
        Assert.Contains(
            $"Groundwork target '{GroundworkTargetNames.Default}' was declared twice against different stores:",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains($"provider '{PrimaryProviderIdentity}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"provider '{SecondProviderIdentity}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Give each physical store its own target name",
            exception.Message,
            StringComparison.Ordinal);

        // The diagnostic must name the stores without ever quoting a connection string.
        Assert.Contains("connection fingerprint", exception.Message, StringComparison.Ordinal);
    }
}
