using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Spec 1229 FR-006: the runtime's step and input warning collector keeps a protected form when asked to.</summary>
public class ExecutionWarningCollectorTests
{
    private static readonly ExecutionWarning Warning = new("VF-PREC-002", "full", Details: new Dictionary<string, string> { ["paths"] = "variables.order.id" })
    {
        ProtectedForm = new ExecutionWarning("VF-PREC-002", "roots", Details: new Dictionary<string, string> { ["paths"] = "variables.order" })
    };

    [Fact]
    public void A_protecting_collector_keeps_the_protected_form_and_an_unprotecting_one_keeps_the_warning()
    {
        var protecting = new ExecutionWarningCollector(protect: true);
        var unprotecting = new ExecutionWarningCollector();

        protecting.Report(Warning);
        unprotecting.Report(Warning);

        Assert.Equal("variables.order", Assert.Single(protecting.Warnings).Details!["paths"]);
        Assert.Same(Warning, Assert.Single(unprotecting.Warnings));
    }
}
