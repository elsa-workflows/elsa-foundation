using System.Reflection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Pins what no provider leaf can prove for the shared suite: its provider-neutral reference boundary, and
/// the scenario matrix every leaf is expected to execute (a silently dropped scenario turns no leaf red).
/// </summary>
public class DesignContractSuiteShapeTests
{
    private static readonly IReadOnlySet<string> AllowedAssemblyNames = new HashSet<string>(
        [
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Activities.Design.Reconciliation.Core",
            "Elsa.Activities.Runtime.Core",
            "Elsa.Events.Core",
            "Elsa.Expressions.Core",
            "Elsa.Locking.Core",
            "Elsa.Pipelines.Core",
            "Elsa.Persistence.Groundwork.DesignConformance.Tests",
            "Elsa.Primitives",
            "Elsa.Primitives.Hosting",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Design.Persistence.Core",
            "Elsa.Workflows.Design.Validations.Core",
            "Elsa.Workflows.Primitives",
            "Elsa.Workflows.Runtime.Core",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.VisualStudio.TestPlatform.ObjectModel",
            "System.Collections",
            "System.ComponentModel",
            "System.Linq",
            "System.Memory",
            "System.Private.CoreLib",
            "System.Runtime",
            "System.Security.Cryptography",
            "System.Text.Json",
            "xunit.assert",
            "xunit.core",
            "Xunit.SkippableFact"
        ],
        StringComparer.Ordinal);

    private static readonly Type[] SuiteTypes =
    [
        typeof(WorkflowDesignContractSuite),
        typeof(ActivityDesignContractSuite),
        typeof(WorkflowDesignQueryContractSuite),
        typeof(ActivityDesignQueryContractSuite),
        typeof(DesignQueryScaleContractSuite),
        typeof(DesignQueryPlanContractSuite),
        typeof(DesignAtomicityContractSuite),
        typeof(DesignIsolationAndRestartContractSuite)
    ];

    [Fact]
    public void Shared_assembly_and_fixture_contracts_are_provider_neutral()
    {
        Assert.All(SuiteTypes, suite => Assert.True(suite.IsAbstract, $"{suite.Name} must be abstract."));

        var referencedAssemblies = Assembly.GetExecutingAssembly()
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!);
        var fixtureTypes = new[] { typeof(IDesignPersistenceContractFixture), typeof(IDesignPersistenceContractFixtureFactory) };
        var signatureAssemblies = fixtureTypes
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(fixtureTypes.SelectMany(type => type.GetProperties()).Select(property => property.PropertyType))
            .SelectMany(ExpandType)
            .Select(type => type.Assembly.GetName().Name!);

        var unexpected = referencedAssemblies
            .Concat(signatureAssemblies)
            .Where(name => !AllowedAssemblyNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            unexpected.Length == 0,
            $"Shared design conformance references must be provider-neutral. Unexpected: {string.Join(", ", unexpected)}.");
    }

    [Theory]
    [InlineData(
        typeof(DesignIsolationAndRestartContractSuite),
        new[]
        {
            nameof(DesignIsolationAndRestartContractSuite.Cross_scope_same_identity_point_read_snapshots_survive_restart),
            nameof(DesignIsolationAndRestartContractSuite.Duplicate_workflow_and_activity_identities_are_rejected_within_a_scope),
            nameof(DesignIsolationAndRestartContractSuite.Foreign_point_reads_are_indistinguishable_from_missing_identities),
            nameof(DesignIsolationAndRestartContractSuite.Foreign_scope_point_writes_are_rejected_without_mutating_either_scope),
            nameof(DesignIsolationAndRestartContractSuite.Reusable_activity_draft_rejects_a_stale_expected_revision_without_replacing_state_or_layout),
            nameof(DesignIsolationAndRestartContractSuite.Same_point_identities_resolve_only_their_own_scope),
            nameof(DesignIsolationAndRestartContractSuite.Single_scope_point_read_snapshot_survives_restart),
            nameof(DesignIsolationAndRestartContractSuite.Workflow_draft_updates_preserve_the_intentional_last_writer_wins_policy)
        })]
    [InlineData(
        typeof(DesignAtomicityContractSuite),
        new[]
        {
            nameof(DesignAtomicityContractSuite.Cancellation_rolls_back_and_propagates_cancellation),
            nameof(DesignAtomicityContractSuite.Duplicate_delivery_does_not_repeat_the_fixture_post_commit_outcome),
            nameof(DesignAtomicityContractSuite.Lost_acknowledgement_after_durable_decision_reconciles_the_authoritative_result_on_retry),
            nameof(DesignAtomicityContractSuite.Non_success_provider_decision_rolls_back_all_staged_parts),
            nameof(DesignAtomicityContractSuite.Over_limit_projected_text_fails_validation_without_persisting),
            nameof(DesignAtomicityContractSuite.Partial_staging_failure_leaves_no_visible_partial_aggregate),
            nameof(DesignAtomicityContractSuite.Same_stable_operation_key_and_canonical_fingerprint_replay_the_prior_result),
            nameof(DesignAtomicityContractSuite.Stable_operation_key_reuse_with_a_different_fingerprint_conflicts_without_mutation)
        })]
    public void Profiled_suite_declares_the_complete_skippable_scenario_matrix(Type suite, string[] expectedScenarios)
    {
        var scenarios = suite
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<FactAttribute>() is not null)
            .ToArray();

        Assert.Equal(expectedScenarios.Order(StringComparer.Ordinal), scenarios.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.All(scenarios, method => Assert.NotNull(method.GetCustomAttribute<SkippableFactAttribute>()));
        Assert.NotNull(suite.GetProperty("ContractProfile", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void Atomicity_fault_plan_rejects_a_non_success_decision_after_the_durable_decision() =>
        Assert.Throws<ArgumentException>(() => new DesignAtomicityFaultPlan(
            DesignAtomicityFaultPhase.AfterDurableDecision,
            DesignAtomicityFaultAction.ReturnNonSuccess));

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in ExpandType(elementType))
                yield return nested;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in ExpandType(argument))
                yield return nested;
        }
    }
}
