using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>The validation of the workflows path (#2197).</summary>
public sealed class GitReconciliationOptionsTests
{
    [Theory]
    [InlineData("workflows")]
    [InlineData("flows/prod")]
    [InlineData("workflows/")]
    [InlineData(".workflows")]
    [InlineData("*")] // git reads it literally: a folder named '*', see GitWorkspaceTests
    public void A_relative_folder_is_a_valid_workflows_path(string workflowsPath) =>
        new GitReconciliationOptions { WorkflowsPath = workflowsPath }.ValidateWorkflowsPath();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("./")]
    [InlineData("../outside")]
    [InlineData("flows/../../outside")]
    [InlineData("/etc")]
    [InlineData("\\share")]
    [InlineData(":/")]
    [InlineData(":(top)")]
    [InlineData(":workflows")]
    public void A_workflows_path_that_would_aim_clean_and_restore_at_more_than_one_folder_is_refused(string workflowsPath) =>
        Assert.Throws<InvalidOperationException>(() => new GitReconciliationOptions { WorkflowsPath = workflowsPath }.ValidateWorkflowsPath());
}
