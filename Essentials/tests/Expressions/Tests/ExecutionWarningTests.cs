using Elsa.Expressions.Core.Models;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>Spec 1228's run-time warning channel and spec 1229 FR-006: a warning's protected form names roots only.</summary>
public class ExecutionWarningTests
{
    private static readonly ExecutionWarning Warning = new("VF-PREC-002", "full", Details: new Dictionary<string, string> { ["paths"] = "variables.order.id" })
    {
        ProtectedForm = new ExecutionWarning("VF-PREC-002", "roots", Details: new Dictionary<string, string> { ["paths"] = "variables.order" })
    };

    [Fact]
    public void An_added_detail_reaches_the_protected_form()
    {
        var withInput = Warning.WithDetail("input", "text");

        Assert.Equal("text", withInput.Details!["input"]);
        Assert.Equal("text", withInput.ProtectedForm!.Details!["input"]);
        Assert.Equal("variables.order", withInput.ProtectedForm.Details["paths"]);
    }

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
