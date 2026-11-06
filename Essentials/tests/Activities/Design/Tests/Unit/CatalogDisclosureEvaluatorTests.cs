using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Disclosure.Extensions;
using Elsa.Activities.Design.Tests.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class CatalogDisclosureEvaluatorTests
{
    [Fact]
    public async Task Public_requires_valid_material_and_initialized_composition_but_no_current_identity()
    {
        using var provider = CatalogDisclosureTestHarness.Services(currentAuthority: false).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>();
        await Assert.ThrowsAsync<CatalogDisclosureUnavailableException>(() => evaluator.EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Public)], CatalogDisclosurePurpose.Metadata).AsTask());
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var results = await evaluator.EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Public),
                CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected())], CatalogDisclosurePurpose.Metadata);
        Assert.Equal([CatalogDisclosureStatus.Allowed, CatalogDisclosureStatus.Unavailable], results.Select(result => result.Status));
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Calls);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("scope")]
    [InlineData("global-with-tenant")]
    [InlineData("tenant-without-id")]
    [InlineData("id")]
    [InlineData("invalid-unicode-id")]
    [InlineData("invalid-unicode-tenant")]
    [InlineData("schema")]
    [InlineData("classification")]
    [InlineData("purpose")]
    public async Task Unknown_or_malformed_resource_facts_are_unavailable_without_policy_calls(string fault)
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var resource = CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Public);
        resource = fault switch
        {
            "kind" => resource with { Kind = "future-kind" },
            "scope" => resource with { OwnerScope = new((CatalogOwnerScopeKind)99, null) },
            "global-with-tenant" => resource with { OwnerScope = new(CatalogOwnerScopeKind.Global, "tenant-a") },
            "tenant-without-id" => resource with { OwnerScope = new(CatalogOwnerScopeKind.Tenant, " ") },
            "id" => resource with { Id = new string('x', 451) },
            "invalid-unicode-id" => resource with { Id = "private-\uD800" },
            "invalid-unicode-tenant" => resource with { OwnerScope = new(CatalogOwnerScopeKind.Tenant, "tenant-\uDC00") },
            "schema" => resource with { Requirement = new(99, CatalogDisclosureKind.Public, []) },
            "classification" => resource with { Requirement = new(1, CatalogDisclosureKind.Unspecified, []) },
            _ => resource
        };
        var decisions = await scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
            [resource], fault == "purpose" ? (CatalogDisclosurePurpose)99 : CatalogDisclosurePurpose.Metadata);
        Assert.Equal(CatalogDisclosureStatus.Unavailable, Assert.Single(decisions).Status);
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task Only_an_explicit_valid_allowed_policy_result_grants(int status)
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Decide = (_, _, _, _) =>
            ValueTask.FromResult(status == -1 ? null! : new CatalogDisclosureDecision((CatalogDisclosureStatus)status));
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var result = await scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected())], CatalogDisclosurePurpose.Metadata);
        Assert.Equal(CatalogDisclosureStatus.Unavailable, Assert.Single(result).Status);
    }

    [Fact]
    public async Task Canonical_owner_id_and_purpose_are_not_replaced_by_the_opaque_policy_binding()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        var state = provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>();
        state.Decide = (resource, binding, purpose, _) =>
        {
            Assert.Equal("version-a", resource.Id);
            Assert.Equal("version", resource.Kind);
            Assert.Equal(new(CatalogOwnerScopeKind.Tenant, "tenant-a"), resource.OwnerScope);
            Assert.Equal(CatalogDisclosurePurpose.Historical, purpose);
            Assert.Equal("opaque-binding-tenant", binding.Resource.GetProperty("tenant").GetString());
            return ValueTask.FromResult(new CatalogDisclosureDecision(CatalogDisclosureStatus.Allowed, "current-revision"));
        };
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var result = await scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected())], CatalogDisclosurePurpose.Historical);
        Assert.Equal(CatalogDisclosureStatus.Allowed, Assert.Single(result).Status);
        Assert.Equal(1, state.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_definite_denial_wins_over_a_missing_or_failing_other_conjunct(bool failure)
    {
        var services = CatalogDisclosureTestHarness.Services();
        services.AddActivityCatalogDisclosurePolicy<CatalogDisclosureTestHarness.Policy>("zzz.deny", "1");
        if (failure)
            services.AddActivityCatalogDisclosurePolicy<CatalogDisclosureTestHarness.Policy>("aaa.unavailable", "1");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Decide = (_, requirement, _, _) =>
            requirement.PolicyKey == "zzz.deny"
                ? ValueTask.FromResult(new CatalogDisclosureDecision(CatalogDisclosureStatus.Denied))
                : throw new IOException("private-owner-failure");
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var result = await scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected("aaa.unavailable", "zzz.deny"))],
            CatalogDisclosurePurpose.Metadata);
        Assert.Equal(CatalogDisclosureStatus.Denied, Assert.Single(result).Status);
        Assert.Equal(failure ? 2 : 1, provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Calls);
    }

    [Fact]
    public async Task A_missing_exact_policy_schema_is_unavailable_instead_of_falling_back_by_key()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var requirement = new CatalogDisclosureRequirement(1, CatalogDisclosureKind.Protected,
            [new("source.policy", "2", System.Text.Json.JsonSerializer.SerializeToElement(new { }))]);
        var result = await scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
            [CatalogDisclosureTestHarness.Resource(requirement)], CatalogDisclosurePurpose.Metadata);
        Assert.Equal(CatalogDisclosureStatus.Unavailable, Assert.Single(result).Status);
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Calls);
    }

    [Fact]
    public async Task A_failing_policy_makes_the_pass_unavailable_with_fixed_message_and_retained_cause()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        var cause = new IOException("private-source-credential-sentinel");
        provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Decide = (_, _, _, _) => throw cause;
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var error = await Assert.ThrowsAsync<CatalogDisclosureUnavailableException>(() => scope.ServiceProvider
            .GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
                [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected())], CatalogDisclosurePurpose.Metadata).AsTask());
        Assert.Same(cause, error.InnerException);
        Assert.Equal(CatalogDisclosureUnavailableException.FixedMessage, error.Message);
        Assert.DoesNotContain("private-source-credential-sentinel", error.Message);
    }

    [Fact]
    public async Task Repeated_calls_in_one_service_scope_observe_current_denial_without_a_positive_cache()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var evaluator = scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>();
        var resources = new[] { CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected()) };
        Assert.Equal(CatalogDisclosureStatus.Allowed, Assert.Single(await evaluator.EvaluateAsync(resources, CatalogDisclosurePurpose.Metadata)).Status);
        var state = provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>();
        state.Decide = (_, _, _, _) => ValueTask.FromResult(new CatalogDisclosureDecision(CatalogDisclosureStatus.Denied));
        Assert.Equal(CatalogDisclosureStatus.Denied, Assert.Single(await evaluator.EvaluateAsync(resources, CatalogDisclosurePurpose.Metadata)).Status);
        Assert.Equal(1, state.Created); // Scoped object reuse is not a cached authorization decision.
        Assert.Equal(2, state.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_authority_lookup_or_policy_evaluation_is_not_wrapped(bool duringPolicy)
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        if (duringPolicy)
            provider.GetRequiredService<CatalogDisclosureTestHarness.PolicyState>().Decide = (_, _, _, _) =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(new CatalogDisclosureDecision(CatalogDisclosureStatus.Allowed));
            };
        else
        {
            var context = provider.GetRequiredService<CatalogDisclosureTestHarness.ContextState>();
            context.Present = false;
            context.BeforePresence = cancellation.Cancel;
        }
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogDisclosureEvaluator>()
            .EvaluateAsync([CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Protected())],
                CatalogDisclosurePurpose.Metadata, cancellation.Token).AsTask());
    }
}
