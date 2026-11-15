using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Preparation;
using Xunit;

namespace Elsa.Etl.Tests;

public sealed partial class EtlBatchProcessorTests
{
    [Fact]
    public async Task Missing_pinned_output_revision_is_classified_as_a_fault()
    {
        using var scenario = await Scenario.CreateAsync(CreatePipeline());
        var scope = scenario.Admission.Invocation.Scope;
        var bindings = new HostFileBindings(
        [
            new HostFileBinding(scope, "main", EtlFileRole.MainOutput, "v2", Path.Combine(scenario.Root, "main-v2"),
                IsCurrent: true, AtomicPublication: true),
            new HostFileBinding(scope, "errors", EtlFileRole.ErrorOutput, "v1", scenario.ErrorRoot,
                IsCurrent: true, AtomicPublication: true)
        ]);

        var result = await Processor.ProcessAsync(
            scenario.Admission, scenario.Pipeline, scenario.Prepared, scenario.Batch, bindings);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.binding-unavailable", result.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.Fault, result.Diagnostic.Kind);
    }
}
