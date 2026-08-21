using Elsa.Workflows.Publishing.Api;
using System.Reflection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Shell-composition obligations for the Publishing API feature.
/// </summary>
/// <remarks>
/// The collectibility cycles that used to live beside this test were retired when the Publishing
/// wire contracts moved back into the implementation assembly. API Explorer retains request and
/// response types for the host service-provider lifetime, so an owner whose contracts ship in its
/// own assembly can no longer be released; see ADR 0069.
/// </remarks>
public sealed class Wave8PublishingFeatureCompositionTests
{
    [Fact]
    public void Publishing_feature_remains_public_nonsealed_and_virtual_for_shell_composition()
    {
        var featureType = typeof(WorkflowsPublishingApiFeature);
        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
        var configure = featureType.GetMethod(nameof(WorkflowsPublishingApiFeature.ConfigureServices), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(configure);
        Assert.True(configure!.IsVirtual);
    }
}
