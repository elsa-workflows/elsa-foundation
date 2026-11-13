using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseSemanticCompositionTests
{
    [Fact]
    public async Task Optional_feature_resolves_scoped_verifier_and_explicitly_validates_registry_composition()
    {
        var fixture = new ReleaseSemanticFixture(); var services = Services();
        services.AddScoped<IReleaseArtifactSemanticVerifier>(_ => fixture.Adapter);
        var feature = new WorkflowReleaseVerificationFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var verifier = first.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>();
        Assert.NotSame(verifier, second.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>());
        Assert.Equal(ReleaseSemanticStatus.Verified, (await verifier.VerifyAsync(fixture.Bundle.Validate())).Status);
    }

    [Theory]
    [InlineData("duplicate-codec")]
    [InlineData("duplicate-site")]
    [InlineData("blank-codec")]
    [InlineData("empty-codecs")]
    [InlineData("blank-identity")]
    [InlineData("blank-site")]
    public void Resolving_framework_fails_ambiguous_or_invalid_claims_before_verification(string change)
    {
        var fixture = new ReleaseSemanticFixture(); var services = Services();
        var first = fixture.Adapter;
        if (change == "blank-codec") first = new([new("", "fixture-json", 1)], (_, _, _, _) => throw new InvalidOperationException("must not invoke"));
        if (change == "empty-codecs") first = new([], (_, _, _, _) => throw new InvalidOperationException("must not invoke"));
        if (change == "blank-identity") first.Identity = new("", "1");
        services.AddScoped<IReleaseArtifactSemanticVerifier>(_ => first);
        if (change == "duplicate-codec") services.AddScoped<IReleaseArtifactSemanticVerifier>(_ => fixture.Adapter);
        if (change is "duplicate-site" or "blank-site")
        {
            var inspector = new ControlledOccurrenceInspector([new("fixture", change == "blank-site" ? "" : "node", 1)], (_, _, _) => throw new InvalidOperationException("must not invoke"));
            services.AddScoped<IReleaseOccurrenceInspector>(_ => inspector);
            if (change == "duplicate-site") services.AddScoped<IReleaseOccurrenceInspector>(_ => inspector);
        }
        var feature = new WorkflowReleaseVerificationFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>());
    }

    [Fact]
    public void Replacement_override_is_preserved_and_duplicate_registration_is_diagnosed()
    {
        var services = Services(); var selected = new ReplacementVerifier(); services.AddScoped<IReleaseSemanticVerifier>(_ => selected);
        var feature = new WorkflowReleaseVerificationFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Same(selected, scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>());
        services.AddScoped<IReleaseSemanticVerifier, ReplacementVerifier>();
        Assert.Contains("Exactly one", Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services)).Message, StringComparison.Ordinal);
        var missingTransport = new ServiceCollection(); missingTransport.AddScoped<IReleaseSemanticVerifier, ReplacementVerifier>();
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(missingTransport));
    }

    [Fact]
    public void Exact_claim_versions_do_not_conflict_and_transport_feature_adds_no_verifier()
    {
        var fixture = new ReleaseSemanticFixture(); var services = Services();
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IReleaseSemanticVerifier));
        var next = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs.Select(x => x with { CodecVersion = x.CodecVersion + 1 }).ToArray(), (_, _, _, _) => throw new InvalidOperationException("must not invoke"));
        services.AddScoped<IReleaseArtifactSemanticVerifier>(_ => fixture.Adapter); services.AddScoped<IReleaseArtifactSemanticVerifier>(_ => next);
        var feature = new WorkflowReleaseVerificationFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection(); var transport = new WorkflowReleaseTransportFeature(); transport.ConfigureServices(services); transport.PostConfigureServices(services); return services;
    }
    private sealed class ReplacementVerifier : IReleaseSemanticVerifier
    {
        public ValueTask<ReleaseSemanticVerificationResult> VerifyAsync(IReleaseBundleTransport bundle, ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ReleaseSemanticVerificationResult(ReleaseSemanticStatus.Incomplete, bundle.Digest, [], [], [], [], []));
    }
}
