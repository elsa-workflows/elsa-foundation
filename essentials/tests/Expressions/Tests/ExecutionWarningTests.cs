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
}
