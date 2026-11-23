using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowExecutableHashContractTests
{
    [Fact]
    public void Each_policy_and_the_input_type_nullability_participate_in_identity()
    {
        var source = LegacyWorkflowHashFixture.Load();
        string Compute(WorkflowValuePolicies policies, WorkflowExecutableInputContract? contract = null) =>
            _hasher.ComputeHash(source.RootActivity, contract ?? source.InputContract!, source.Dependencies,
                source.PersistenceMode, source.WorkflowVariables, source.IncidentStrategy, policies);

        Assert.Equal(Hash(source), Compute(WorkflowValuePolicies.Off));
        var hashes = new[] { Compute(new()), Compute(new(true, false)), Compute(new(false, true)), Compute(new(true, true)) };
        Assert.Equal(4, hashes.Distinct(StringComparer.Ordinal).Count());
        var nullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("String"), false, isNullable: true)]);
        var nonNullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("Int32"), false)]);
        Assert.NotEqual(Compute(new(), nullable), Compute(new(), nonNullable));
        var sameTypeNonNullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("String"), false)]);
        Assert.NotEqual(Compute(new(), nullable), Compute(new(), sameTypeNonNullable));
    }
}
