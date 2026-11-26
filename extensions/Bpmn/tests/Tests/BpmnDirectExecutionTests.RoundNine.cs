using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task An_already_queued_native_result_preserves_cancellation_owed_from_an_earlier_scope_checkpoint()
    {
        var retained = await RestoreCurrentPriorCancellationAsync();
        await using var host = await OpenAsync(admitNewExecutions: false, configure: ConfigurePriorCancellationServices);
        await RecoverPriorCancellationAsync(host, retained.Outer, retained.Middle, retained.Inner, retained.Source, retained.Custody);
    }
}
