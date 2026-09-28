using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Core.Services;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.Tests.Core;

/// <summary>
/// Spec 182's shared dormancy check (FR-003 to FR-005, FR-008, FR-011 to FR-014): a requirement on family F at version V
/// is met only once this host writes F at V or later and, for a completeness requirement, once the finish record it
/// observed names V or later. Each case is tested both ways, since the dangerous direction is the one that answers
/// "available" too early.
/// </summary>
public sealed class SchemaDormancyCheckTests
{
    private const string Family = "Orders";
    private static readonly SchemaVersionRequirement NeedsTwo = new(Family, "2");
    private static readonly SchemaVersionRequirement NeedsTwoComplete = new(Family, "2", requiresCompleteness: true);
    private readonly FakeObservedSchemaFinalization _observed = new();
    private readonly SchemaDormancyCheck _check;

    public SchemaDormancyCheckTests() => _check = new SchemaDormancyCheck(Options.Create(new SchemaDormancyOptions()), _observed);

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    public void A_requirement_is_met_once_this_host_writes_the_version_or_a_later_one(string writeVersion)
    {
        _observed.Set(Observed(writeVersion));

        Assert.True(_check.Evaluate([NeedsTwo]).IsAvailable);
    }

    [Fact]
    public void A_requirement_is_unmet_while_this_host_writes_an_older_version_and_says_every_host_must_read_it()
    {
        _observed.Set(Observed("1"));

        var unmet = Assert.Single(_check.Evaluate([NeedsTwo]).Unmet);

        Assert.Equal((SchemaDormancyKind.WaitingForHosts, "1"), (unmet.Kind, unmet.ObservedVersion));
        Assert.Contains("once every host can read version '2' of schema family 'Orders'", unmet.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_version_keeps_the_requirement_unmet_and_says_so_without_the_operators_words_to_a_caller()
    {
        var hold = new SchemaHoldObservation("2", "canary on node-7", "ops@example.com", DateTimeOffset.UnixEpoch);
        _observed.Set(Observed("1") with { Pending = [new SchemaPendingVersion("2", false, [hold], null)] });

        var unmet = Assert.Single(_check.Evaluate([NeedsTwo]).Unmet);

        Assert.Equal(SchemaDormancyKind.Held, unmet.Kind);
        Assert.Contains("held by an operator", unmet.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("node-7", unmet.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("ops@example.com", unmet.Reason, StringComparison.Ordinal);
        var forOperator = SchemaDormancyReasons.ForOperator(unmet, null);
        Assert.Contains("canary on node-7", forOperator, StringComparison.Ordinal);
        Assert.Contains("ops@example.com", forOperator, StringComparison.Ordinal);
    }

    [Fact]
    public void A_finalized_version_this_host_has_not_adopted_keeps_the_requirement_unmet()
    {
        _observed.Set(Observed("1") with { FinalizedVersion = "2" });

        Assert.Equal(SchemaDormancyKind.NotYetAdopted, Assert.Single(_check.Evaluate([NeedsTwo]).Unmet).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    public void A_completeness_requirement_stays_unmet_after_finalization_until_the_finish_record_names_the_version(string? completion)
    {
        _observed.Set(Observed("2") with { CompletionVersion = completion });

        var unmet = Assert.Single(_check.Evaluate([NeedsTwoComplete]).Unmet);

        Assert.Equal(SchemaDormancyKind.WaitingForCompleteness, unmet.Kind);
        Assert.Contains("existing records of schema family 'Orders' have been upgraded to version '2'", unmet.Reason, StringComparison.Ordinal);
        Assert.True(_check.Evaluate([NeedsTwo]).IsAvailable, "Without a completeness requirement, finalization alone is enough.");
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    public void A_completeness_requirement_is_met_once_the_finish_record_names_the_version_or_a_later_one(string completion)
    {
        _observed.Set(Observed("3") with { CompletionVersion = completion });

        Assert.True(_check.Evaluate([NeedsTwoComplete]).IsAvailable);
    }

    [Fact]
    public void A_completion_version_this_build_cannot_place_is_incomplete()
    {
        _observed.Set(Observed("2") with { CompletionVersion = "0" });

        Assert.Equal(SchemaDormancyKind.WaitingForCompleteness, Assert.Single(_check.Evaluate([NeedsTwoComplete]).Unmet).Kind);
    }

    [Fact]
    public void A_family_this_host_has_observed_nothing_of_is_unmet_rather_than_assumed_available()
    {
        Assert.Equal(SchemaDormancyKind.NotObserved, Assert.Single(_check.Evaluate([NeedsTwo]).Unmet).Kind);
        Assert.Equal(SchemaDormancyKind.NotObserved, Assert.Single(new SchemaDormancyCheck().Evaluate([NeedsTwo]).Unmet).Kind);
    }

    [Fact]
    public void A_version_this_build_does_not_read_is_unmet()
    {
        _observed.Set(Observed("3"));

        Assert.Equal(SchemaDormancyKind.UnknownVersion, Assert.Single(_check.Evaluate([new SchemaVersionRequirement(Family, "4")]).Unmet).Kind);
    }

    [Fact]
    public void A_family_whose_writes_are_refused_is_unmet_whatever_version_it_wrote()
    {
        _observed.Set(Observed("2") with { WritesRefused = true, FinalizedVersion = "9" });

        Assert.Equal(SchemaDormancyKind.WritesRefused, Assert.Single(_check.Evaluate([NeedsTwo]).Unmet).Kind);
    }

    [Fact]
    public void A_feature_that_needs_several_families_is_dormant_until_every_one_is_met_and_its_reason_lists_each_unmet_one()
    {
        _observed.Set(Observed("1"));
        _observed.Set(Observed("1") with { Family = "Lines", CompletionVersion = "1" });
        SchemaVersionRequirement[] requirements = [NeedsTwo, new("Lines", "2", requiresCompleteness: true)];

        var dormant = _check.Evaluate(requirements);
        Assert.Equal(2, dormant.Unmet.Count);
        Assert.Contains("'Orders'", dormant.Reason, StringComparison.Ordinal);
        Assert.Contains("'Lines'", dormant.Reason, StringComparison.Ordinal);

        _observed.Set(Observed("2"));
        Assert.Equal("Lines", Assert.Single(_check.Evaluate(requirements).Unmet).Requirement.Family);

        _observed.Set(Observed("2") with { Family = "Lines", CompletionVersion = "2" });
        Assert.True(_check.Evaluate(requirements).IsAvailable);
    }

    [Fact]
    public async Task An_available_answer_reads_nothing()
    {
        _observed.Set(Observed("2"));

        await _check.EnsureAvailableAsync([NeedsTwo], "OrdersApi");

        Assert.Equal(0, _observed.Refreshes);
    }

    /// <summary>FR-014: a stale view does not refuse a request that finalization already allows.</summary>
    [Fact]
    public async Task Before_refusing_the_check_refreshes_a_stale_observation_and_serves_what_it_then_allows()
    {
        _observed.Set(Observed("1"));
        _observed.OnRefresh = () => _observed.Set(Observed("2"));

        await _check.EnsureAvailableAsync([NeedsTwo], "OrdersApi");

        Assert.Equal(1, _observed.Refreshes);
        Assert.Equal(new SchemaDormancyOptions().RefreshBound, _observed.LastMaxAge);
    }

    [Fact]
    public async Task A_requirement_still_unmet_after_the_refresh_is_refused_with_the_feature_the_versions_and_a_caller_neutral_reason()
    {
        _observed.Set(Observed("1"));

        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => _check.EnsureAvailableAsync([NeedsTwo], "OrdersApi").AsTask());

        Assert.Equal(1, _observed.Refreshes);
        Assert.Equal((Family, "1", "2", "OrdersApi"), (refusal.Family, refusal.WriteVersion, refusal.RequiredVersion, refusal.FeatureId));
        Assert.Equal(SchemaWriteRefusedException.RefusalCode, refusal.Code);
        Assert.Contains("once every host can read version '2'", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_family_this_host_has_observed_nothing_of_is_refused_naming_no_version_it_does_not_have()
    {
        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => new SchemaDormancyCheck().EnsureAvailableAsync([NeedsTwo]).AsTask());

        Assert.Equal(("(none)", null), (refusal.WriteVersion, refusal.FeatureId));
    }

    /// <summary>FR-011: members and hold reasons reach operators, never callers.</summary>
    [Fact]
    public void The_operator_reason_carries_the_gates_status_and_the_caller_reason_names_no_host()
    {
        var intent = new SchemaIntentObservation("2", "host-a (1a2b)", DateTimeOffset.UnixEpoch);
        var status = Observed("1") with
        {
            FinalizedVersion = "1",
            Intent = intent,
            Pending = [new SchemaPendingVersion("2", false, [], ["host-b (3c4d) reads [1]"])]
        };
        _observed.Set(Observed("1"));
        var unmet = Assert.Single(_check.Evaluate([NeedsTwo]).Unmet);

        var forOperator = SchemaDormancyReasons.ForOperator(unmet, status);

        Assert.Contains("host-b (3c4d) reads [1]", forOperator, StringComparison.Ordinal);
        Assert.Contains("finalized at '1'", forOperator, StringComparison.Ordinal);
        Assert.Contains("host-a (1a2b)", forOperator, StringComparison.Ordinal);
        Assert.DoesNotContain("host-", unmet.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_feature_declares_its_requirements_on_its_class_and_they_are_read_from_it()
    {
        var declared = SchemaVersionRequirement.DeclaredBy(typeof(DormantProbeFeature));

        Assert.Equal([new SchemaVersionRequirement("Orders", "2"), new SchemaVersionRequirement("Lines", "3", requiresCompleteness: true)], declared);
        Assert.Empty(SchemaVersionRequirement.DeclaredBy(typeof(SchemaDormancyCheckTests)));
    }

    [Fact]
    public void The_default_check_resolves_the_source_composed_beside_it()
    {
        var services = new ServiceCollection().TryAddSchemaDormancyCheck().AddObservedSchemaFinalization<FakeObservedSchemaFinalization>();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(SchemaDormancyKind.NotObserved, Assert.Single(provider.GetRequiredService<ISchemaDormancyCheck>().Evaluate([NeedsTwo]).Unmet).Kind);
        Assert.Same(provider.GetRequiredService<IObservedSchemaFinalization>(), provider.GetRequiredService<IObservedSchemaFinalization>());
    }

    /// <summary>§2.6.2: a second implementation of either replacement contract fails where it is composed.</summary>
    [Fact]
    public void A_second_source_or_check_is_refused_where_it_is_composed_and_the_same_one_again_is_not()
    {
        var services = new ServiceCollection().TryAddSchemaDormancyCheck().AddObservedSchemaFinalization<FakeObservedSchemaFinalization>();

        services.AddObservedSchemaFinalization<FakeObservedSchemaFinalization>();
        Assert.Throws<InvalidOperationException>(() => services.AddObservedSchemaFinalization<OtherObservedSchemaFinalization>());

        services.AddSchemaDormancyCheck<OtherCheck>();
        Assert.Equal(typeof(OtherCheck), Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISchemaDormancyCheck)).ImplementationType);
        services.TryAddSchemaDormancyCheck();
        Assert.Throws<InvalidOperationException>(() => services.AddSchemaDormancyCheck<SchemaDormancyCheck>());
    }

    private static SchemaFamilyObservation Observed(string writeVersion) =>
        new(Family, "Tests.Orders", ["1", "2", "3"], writeVersion, writeVersion, false, "1", [], null, DateTimeOffset.UnixEpoch);

    [RequiresSchemaVersion("Orders", "2")]
    [RequiresSchemaVersion("Lines", "3", RequiresCompleteness = true)]
    private sealed class DormantProbeFeature;

    private sealed class OtherObservedSchemaFinalization : FakeObservedSchemaFinalization;

    private sealed class OtherCheck : ISchemaDormancyCheck
    {
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaAvailability.Available;

        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SchemaAvailability.Available);

        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];

        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SchemaFamilyObservation>>([]);
    }
}

/// <summary>An observation a test sets directly, counting the refreshes the check asks for.</summary>
internal class FakeObservedSchemaFinalization : IObservedSchemaFinalization
{
    private readonly Dictionary<string, SchemaFamilyObservation> _families = new(StringComparer.Ordinal);

    public int Refreshes { get; private set; }

    public TimeSpan? LastMaxAge { get; private set; }

    public Action? OnRefresh { get; set; }

    public void Set(SchemaFamilyObservation family) => _families[family.Family] = family;

    public SchemaFamilyObservation? Find(string family) => _families.GetValueOrDefault(family);

    public IReadOnlyList<SchemaFamilyObservation> Observe() => _families.Values.ToArray();

    public ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        Refreshes++;
        LastMaxAge = maxAge;
        OnRefresh?.Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Observe());
}
