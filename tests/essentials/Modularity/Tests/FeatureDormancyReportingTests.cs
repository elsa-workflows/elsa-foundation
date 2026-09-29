using System.Security.Claims;
using CShells.Features;
using Elsa.Attention.Core;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Core.Services;
using Elsa.Cluster.InProcess;
using Elsa.Modularity.Api.Attention;
using Elsa.Modularity.Api.Services;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Spec 182, User Story 1, FR-008 to FR-011 and SC-002: an enabled feature that declares a dormancy requirement stays in
/// the catalog, enabled and dormant, with the reason and the finalization gate's status; Attention holds one informational
/// item for it until it is available; and a family whose writes this host refuses is reported as critical.
/// </summary>
public sealed class FeatureDormancyReportingTests
{
    private const string Family = "Orders";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T10:00:00Z");
    private readonly FakeObservedSchemaFinalization _observed = new();
    private readonly SchemaDormancyCheck _check;

    public FeatureDormancyReportingTests() => _check = new SchemaDormancyCheck(Options.Create(new SchemaDormancyOptions()), _observed);

    [Fact]
    public async Task An_enabled_feature_waiting_for_hosts_is_listed_as_enabled_and_dormant_with_the_family_version_and_gate_status()
    {
        _observed.Set(Observed("1"), blockers: ["host-c (9f) reads [1]"]);

        var item = await CatalogItemAsync(enabled: true);

        Assert.True(item.Enabled);
        Assert.Null(item.ReadError);
        var availability = Assert.IsType<FeatureAvailability>(item.Availability);
        Assert.Equal(FeatureAvailability.DormantStatus, availability.Status);
        var reason = Assert.Single(availability.Reasons);
        Assert.Equal((Family, "2", nameof(SchemaDormancyKind.WaitingForHosts)), (reason.Family, reason.Version, reason.Kind));
        Assert.Contains("once every host can read version '2' of schema family 'Orders'", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("host-c (9f) reads [1]", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("finalized at '1'", reason.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_held_feature_names_the_hold_its_reason_and_who_placed_it()
    {
        var hold = new SchemaHoldObservation(null, "canary of 2", "ops@example", Now);
        _observed.Set(Observed("1") with { Pending = [new SchemaPendingVersion("2", false, [hold], null)] });

        var reason = Assert.Single((await CatalogItemAsync(enabled: true)).Availability!.Reasons);

        Assert.Equal(nameof(SchemaDormancyKind.Held), reason.Kind);
        Assert.Contains("canary of 2", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("ops@example", reason.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_the_version_is_finalized_and_observed_the_same_feature_is_available()
    {
        _observed.Set(Observed("1"));
        Assert.True((await CatalogItemAsync(enabled: true)).Availability!.IsDormant);

        _observed.Set(Observed("2"));

        Assert.Equal(FeatureAvailability.Available, (await CatalogItemAsync(enabled: true)).Availability);
    }

    [Fact]
    public async Task Dormancy_is_reported_only_for_enabled_features_and_a_feature_that_declares_nothing_is_available()
    {
        _observed.Set(Observed("1"));

        Assert.Null((await CatalogItemAsync(enabled: false)).Availability);
        Assert.Equal(FeatureAvailability.Available, (await CatalogItemAsync(enabled: true, typeof(PlainFeature))).Availability);
    }

    /// <summary>A status that cannot be read costs the fleet's detail, never the dormancy itself.</summary>
    [Fact]
    public async Task A_status_that_cannot_be_read_still_lists_the_feature_as_dormant_with_its_reason()
    {
        _observed.Set(Observed("1"));
        _observed.FailStatus = true;

        var reason = Assert.Single((await CatalogItemAsync(enabled: true)).Availability!.Reasons);

        Assert.Contains("once every host can read version '2'", reason.Reason, StringComparison.Ordinal);
    }

    /// <summary>A host that composes no check cannot tell, so the feature is dormant, never assumed available.</summary>
    [Fact]
    public async Task A_host_that_composes_no_check_lists_a_feature_that_declares_a_requirement_as_dormant()
    {
        var reason = Assert.Single((await CatalogItemAsync(enabled: true, check: null)).Availability!.Reasons);

        Assert.Equal(nameof(SchemaDormancyKind.NotObserved), reason.Kind);
    }

    [Fact]
    public async Task The_catalog_reads_no_status_when_nothing_is_dormant()
    {
        _observed.Set(Observed("2"));

        await CatalogItemAsync(enabled: true);

        Assert.Equal(0, _observed.StatusReads);
    }

    /// <summary>User Story 1, acceptance 2 and 3.</summary>
    [Fact]
    public async Task Attention_holds_one_info_item_for_a_dormant_feature_and_none_once_it_is_available()
    {
        _observed.Set(Observed("1"));
        var dormant = await AttentionAsync(await CatalogItemAsync(enabled: true));

        var item = Assert.Single(dormant.Items);
        Assert.Equal((AttentionSeverity.Info, "feature-dormant:OrdersApi"), (item.Severity, item.Id));
        Assert.Contains("OrdersApi", item.Title, StringComparison.Ordinal);
        Assert.Contains("once every host can read version '2'", item.Summary, StringComparison.Ordinal);
        Assert.Contains(item.Correlations, correlation => correlation == new AttentionCorrelation("schema-family", Family));

        _observed.Set(Observed("2"));
        Assert.Empty((await AttentionAsync(await CatalogItemAsync(enabled: true))).Items);
    }

    /// <summary>FR-010 and spec 181, FR-012: not dormancy, and never merely informational.</summary>
    [Fact]
    public async Task Attention_reports_a_family_whose_writes_this_host_refuses_as_critical()
    {
        _observed.Set(Observed("2") with { FinalizedVersion = "9", WritesRefused = true });

        var contribution = await AttentionAsync();

        var item = Assert.Single(contribution.Items);
        Assert.Equal((AttentionSeverity.Critical, $"schema-family-writes-refused:{Family}"), (item.Severity, item.Id));
        Assert.Contains("'9'", item.Summary, StringComparison.Ordinal);
    }

    /// <summary>The runtime contributor hands each feature's class on, which is where its requirements are read from.</summary>
    private Task<FeatureCatalogItem> CatalogItemAsync(bool enabled, Type? featureType = null) => CatalogItemAsync(enabled, _check, featureType);

    private static async Task<FeatureCatalogItem> CatalogItemAsync(bool enabled, ISchemaDormancyCheck? check, Type? featureType = null)
    {
        var context = FeatureCatalogTestContext.Create();
        await new RuntimeFeatureCatalogContributor(new FakeRuntimeFeatureCatalog(
            new ShellFeatureDescriptor("OrdersApi") { StartupType = featureType ?? typeof(OrdersApiFeature) })).ContributeAsync(context);
        context.Items["OrdersApi"].Enabled = enabled;

        await new FeatureAvailabilityCatalogContributor(check).ContributeAsync(context);

        return context.Items["OrdersApi"].ToItem();
    }

    private Task<AttentionContribution> AttentionAsync(params FeatureCatalogItem[] features) =>
        new ModularityAttentionContributor(new CatalogOf(features), new FakeTimeProvider(Now), _check)
            .EvaluateAsync(new(new(new ClaimsPrincipal(), null), new(1), new Dictionary<string, string>()))
            .AsTask();

    private static SchemaFamilyObservation Observed(string writeVersion) =>
        new(Family, "Tests.Orders", ["1", "2"], writeVersion, writeVersion, false, writeVersion, [], null, Now);

    [RequiresSchemaVersion(Family, "2")]
    private sealed class OrdersApiFeature;

    private sealed class PlainFeature;

    private sealed class CatalogOf(IReadOnlyList<FeatureCatalogItem> features) : IFeatureManagementService
    {
        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureCatalogResponse("revision", features));

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
