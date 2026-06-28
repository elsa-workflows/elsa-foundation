using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Visible-scope, name-based variable access (#212) — the resolution that backs JavaScript
/// <c>getX()</c>/<c>setX()</c>/<c>getVariable</c>/<c>setVariable</c> helpers. Names resolve through
/// the visible scope chain (nearest wins, shadowing allowed) and writes target the correct scope,
/// while structured references stay explicit.
/// </summary>
public sealed class VisibleScopeNameAccessTests
{
    private static IReadOnlyDictionary<string, IVariable> Vars(params (string Key, string Name, object? Value)[] variables) =>
        variables.ToDictionary(v => v.Key, v => (IVariable)new Variable(v.Name, v.Value), StringComparer.Ordinal);

    private static VariableScope Chain()
    {
        var workflow = new VariableScope(VariableReference.WorkflowScopeId, Vars(("var-wf", "Greeting", "hello")));
        var outer = new VariableScope("outer", Vars(("var-outer", "Counter", 1)), workflow);
        return new VariableScope("inner", Vars(("var-inner", "Counter", 2)), outer);
    }

    [Fact]
    public void Read_by_name_resolves_nearest_scope_with_shadowing()
    {
        var chain = Chain();

        Assert.True(chain.TryGetValueByName("Counter", out var counter));
        Assert.Equal(2, counter); // inner shadows outer
        Assert.True(chain.TryGetValueByName("Greeting", out var greeting));
        Assert.Equal("hello", greeting); // workflow variable visible from within containers
    }

    [Fact]
    public void Write_by_name_targets_the_nearest_visible_scope()
    {
        var chain = Chain();

        Assert.True(chain.TrySetValueByName("Counter", 99));

        // Nearest (inner) scope updated; the shadowed outer scope is untouched.
        Assert.True(chain.TryGetValueByName("Counter", out var nearest));
        Assert.Equal(99, nearest);
        Assert.True(chain.TryGetValue(new VariableReference("var-outer", "outer"), out var outer));
        Assert.Equal(1, outer);
    }

    [Fact]
    public void Write_by_name_to_a_workflow_variable_targets_the_workflow_scope()
    {
        var chain = Chain();

        Assert.True(chain.TrySetValueByName("Greeting", "updated"));

        Assert.True(chain.TryGetValue(new VariableReference("var-wf", VariableReference.WorkflowScopeId), out var value));
        Assert.Equal("updated", value);
    }

    [Fact]
    public void Enumerate_visible_variables_is_nearest_first_with_shadowed_names_omitted()
    {
        var names = Chain().EnumerateVisibleVariables().Select(v => v.Name).ToArray();

        // "Counter" appears once (inner shadows outer); "Greeting" from the workflow scope.
        Assert.Equal(new[] { "Counter", "Greeting" }, names);
    }

    [Fact]
    public void Name_based_write_does_not_change_the_structured_reference_contract()
    {
        var chain = Chain();

        chain.TrySetValueByName("Counter", 42);

        // The structured reference to the inner scope reflects the write; the outer scope's
        // structured value is unchanged. Name convenience and explicit references stay consistent.
        Assert.True(chain.TryGetValue(new VariableReference("var-inner", "inner"), out var inner));
        Assert.Equal(42, inner);
        Assert.True(chain.TryGetValue(new VariableReference("var-outer", "outer"), out var outer));
        Assert.Equal(1, outer);
    }
}
