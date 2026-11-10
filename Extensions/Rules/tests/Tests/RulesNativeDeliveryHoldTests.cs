using Elsa.Rules.Workflows.Runtime.Services;

namespace Elsa.Rules.Tests;

public sealed class RulesNativeDeliveryHoldTests
{
    [Fact]
    public async Task A_missing_or_inactive_hold_file_never_delays_delivery()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-rules-hold-");
        try
        {
            var path = Path.Combine(directory.FullName, "hold");
            await RulesNativeDeliveryExecution.HoldNativeDeliveryForTestAsync(null, CancellationToken.None);
            await RulesNativeDeliveryExecution.HoldNativeDeliveryForTestAsync(path, CancellationToken.None);
            await File.WriteAllTextAsync(path, "release");
            await RulesNativeDeliveryExecution.HoldNativeDeliveryForTestAsync(path, CancellationToken.None);
            Assert.False(File.Exists(path + ".engaged"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_hold_claim_file_marks_engagement_and_holds_until_cancelled()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-rules-hold-");
        try
        {
            var path = Path.Combine(directory.FullName, "hold");
            await File.WriteAllTextAsync(path, RulesNativeDeliveryExecution.HoldClaimMode);
            using var cancellation = new CancellationTokenSource();
            var hold = RulesNativeDeliveryExecution.HoldNativeDeliveryForTestAsync(path, cancellation.Token).AsTask();
            // The marker is written before the wait begins, so it appears without any cancellation; the hold itself stays
            // pending until the token is cancelled.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(path + ".engaged") && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.True(File.Exists(path + ".engaged"));
            Assert.False(hold.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
