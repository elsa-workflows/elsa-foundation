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
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await RulesNativeDeliveryExecution.HoldNativeDeliveryForTestAsync(path, cancellation.Token));
            Assert.True(File.Exists(path + ".engaged"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
