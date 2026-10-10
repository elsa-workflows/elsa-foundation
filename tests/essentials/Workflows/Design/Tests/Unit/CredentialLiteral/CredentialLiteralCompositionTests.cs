using System.Reflection;
using CShells.Features;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Reconciliation.Git;
using Elsa.Workflows.Design.Reconciliation.Json;
using Elsa.Workflows.Design.Validations;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.CredentialLiteral;

/// <summary>
/// Spec 188, FR-008: every feature whose services admit workflow state through the credential-literal rule depends on
/// the feature that registers the rule, so a host that composes one without the rule fails at composition instead of
/// skipping it.
/// </summary>
public sealed class CredentialLiteralCompositionTests
{
    public static TheoryData<Type> AdmittingFeatures => new()
    {
        typeof(WorkflowsDesignApiFeature),
        typeof(JsonWorkflowReconciliationFeature),
        typeof(WorkflowsDesignGitReconciliationFeature)
    };

    [Theory]
    [MemberData(nameof(AdmittingFeatures))]
    public void A_feature_that_admits_workflow_state_depends_on_the_validations_feature(Type feature)
    {
        var validations = typeof(WorkflowDesignValidationsFeature).GetCustomAttribute<ShellFeatureAttribute>()!.Name;
        var dependsOn = feature.GetCustomAttribute<ShellFeatureAttribute>()!.DependsOn.Select(dependency => dependency.ToString());

        Assert.Contains(validations, dependsOn);
    }
}
