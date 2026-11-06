using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Disclosure.Extensions;
using Elsa.Activities.Design.Tests.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class CatalogAdmissionTests
{
    [Fact]
    public async Task Exact_current_origin_admits_canonical_protection_and_immutable_inheritance()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        var admission = scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>();
        var candidate = CatalogDisclosureTestHarness.Candidate(inherited: CatalogDisclosureTestHarness.Protected("inherited.policy"));
        var admitted = await admission.AdmitAsync(origin, candidate);
        Assert.Equal(CatalogDisclosureKind.Protected, admitted.Kind);
        Assert.Equal(["inherited.policy", "source.policy"], admitted.Policies.Select(policy => policy.PolicyKey));
        await admission.ValidateAsync(origin, candidate, admitted);
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => admission.ValidateAsync(origin, candidate,
            CatalogDisclosureTestHarness.Public).AsTask());
    }

    [Fact]
    public async Task Fake_same_type_binding_and_another_scopes_origin_are_refused_before_classification()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var first = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(first.ServiceProvider);
        var origin = first.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        using var second = provider.CreateScope();
        var admission = second.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>();
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => admission.AdmitAsync(origin, CatalogDisclosureTestHarness.Candidate()).AsTask());
        var fake = new CatalogAdmissionOrigin("source", new CatalogDisclosureTestHarness.Producer(new()));
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => admission.AdmitAsync(fake, CatalogDisclosureTestHarness.Candidate()).AsTask());
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Calls);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("global")]
    [InlineData("unspecified")]
    [InlineData("mismatched-child")]
    public async Task Actual_owner_facts_must_match_both_materials_and_trusted_current_scope(string fault)
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        var candidate = fault switch
        {
            "foreign" => CatalogDisclosureTestHarness.Candidate(new(CatalogOwnerScopeKind.Tenant, "tenant-b")),
            "global" => CatalogDisclosureTestHarness.Candidate(new(CatalogOwnerScopeKind.Global, null)),
            "unspecified" => CatalogDisclosureTestHarness.Candidate(new(CatalogOwnerScopeKind.Unspecified, null)),
            _ => CatalogDisclosureTestHarness.Candidate() with { VersionOwner = new(CatalogOwnerScopeKind.Tenant, "tenant-b") }
        };
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>()
            .AdmitAsync(origin, candidate).AsTask());
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Calls);
    }

    [Fact]
    public async Task An_explicit_global_profile_can_admit_public_material_without_current_user_identity()
    {
        var services = CatalogDisclosureTestHarness.Services(producer: false, currentAuthority: false);
        services.AddActivityCatalogProducer<CatalogDisclosureTestHarness.Producer, CatalogDisclosureTestHarness.Owner>(
            "global", CatalogProducerOwnerScope.Global);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Result = CatalogDisclosureTestHarness.Public;
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("global");
        var result = await scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>().AdmitAsync(origin,
            CatalogDisclosureTestHarness.Candidate(new(CatalogOwnerScopeKind.Global, null)));
        Assert.Equal(CatalogDisclosureKind.Public, result.Kind);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("unknown")]
    [InlineData("wrong-policy")]
    [InlineData("wrong-schema")]
    public async Task A_forced_protected_owner_cannot_weaken_its_classification_even_with_protected_lineage(string fault)
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        var state = provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>();
        state.Result = fault switch
        {
            "public" => CatalogDisclosureTestHarness.Public,
            "unknown" => new(1, CatalogDisclosureKind.Unspecified, []),
            "wrong-policy" => CatalogDisclosureTestHarness.Protected("other.policy"),
            _ => new(1, CatalogDisclosureKind.Protected, [new("source.policy", "2", System.Text.Json.JsonSerializer.SerializeToElement(new { }))])
        };
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>()
            .AdmitAsync(origin, CatalogDisclosureTestHarness.Candidate(inherited: CatalogDisclosureTestHarness.Protected())).AsTask());
    }

    [Fact]
    public async Task Owner_failure_is_wrapped_with_fixed_message_and_original_cause()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        var cause = new IOException("private-source-credential-sentinel");
        provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Failure = cause;
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        var error = await Assert.ThrowsAsync<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>()
            .AdmitAsync(origin, CatalogDisclosureTestHarness.Candidate()).AsTask());
        Assert.Same(cause, error.InnerException);
        Assert.Equal(CatalogAdmissionException.FixedMessage, error.Message);
        Assert.DoesNotContain("private-source-credential-sentinel", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_owner_or_producer_resolution_remains_cancellation(bool producer)
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        CatalogAdmissionOrigin origin;
        if (producer)
        {
            provider.GetRequiredService<CatalogDisclosureTestHarness.SourceState>().BeforeCreation = () =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };
            origin = new("source", new object());
        }
        else
        {
            origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
            provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Before = cancellation.Cancel;
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>()
            .AdmitAsync(origin, CatalogDisclosureTestHarness.Candidate(), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Malformed_unicode_in_material_identity_is_refused_before_classification()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var origin = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source");
        var candidate = CatalogDisclosureTestHarness.Candidate();
        ((Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion)candidate.Version).Id = "version-\uD800";
        await Assert.ThrowsAsync<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<IActivityCatalogAdmission>()
            .AdmitAsync(origin, candidate).AsTask());
        Assert.Equal(0, provider.GetRequiredService<CatalogDisclosureTestHarness.OwnerState>().Calls);
    }
}
