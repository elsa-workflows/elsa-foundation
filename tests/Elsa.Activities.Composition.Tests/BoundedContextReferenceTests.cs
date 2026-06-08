using Elsa.Activities.Composition.Runtime.Constructors;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

public class BoundedContextReferenceTests
{
    [Fact] // US2 AS3 / SC-001 / Elsa §E2.2 — the runtime activity carries no Design dependency
    public void CompositionRuntime_ReferencesNoDesignAssembly()
    {
        var assembly = typeof(WorkflowActivityConstructor).Assembly;

        var designReferences = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Elsa.", StringComparison.Ordinal) && name.Contains(".Design", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(designReferences);
    }
}
