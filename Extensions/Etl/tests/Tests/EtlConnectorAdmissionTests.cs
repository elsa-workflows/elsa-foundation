using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Theory]
    [InlineData(false, "etl.binding-unavailable")]
    [InlineData(true, "etl.executable.limits-exceeded")]
    public async Task Connector_admission_uses_runtime_validation_and_retains_refusal_for_delivery(
        bool lowerHostCeiling, string expectedCode)
    {
        await using var files = new RuntimeFiles("id,name\n");
        var invocation = ConnectorInvocation(files.Scope, "connector-admission");
        var first = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false,
            configureServices: services =>
            {
                if (lowerHostCeiling)
                    services.AddSingleton(EtlRuntimeOptions.Create(new EtlRuntimeFeature
                    {
                        SnapshotRoot = files.SnapshotRoot,
                        HttpLimits = new EtlHttpHostLimits { MaxAttempts = 1 }
                    }));
            });
        await using (var operation = first.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var admission = services.GetRequiredService<IEtlRunAdmissionService>();
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var result = await admission.AdmitAsync(invocation);
            Assert.Equal(EtlStoreStatus.Replay, result.Status);
            var run = Assert.IsType<EtlRunView>(result.Run);
            Assert.Equal(EtlRunStatus.BusinessError, run.Status);
            Assert.Equal(expectedCode, run.TerminalResult!.Diagnostic!.Code);
            Assert.Equal(EtlStoreStatus.Replay, (await admission.AdmitAsync(invocation)).Status);
            await AssertNoFileWorkAsync(services, store, invocation);
            Assert.False(Directory.Exists(files.SnapshotRoot));
        }

        await files.StopHostAsync(first);
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var reopened = await files.StartHostAsync(EfMigratePolicy.Validate, includeBindings: false, resultDelivery: delivery);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var received = await delivery.WaitForDeliveryAsync(timeout.Token);
        Assert.Equal(invocation.RunId, received.RunId);
        Assert.Equal(expectedCode, received.Result.Diagnostic!.Code);
        await files.WaitForOutboxAsync(reopened.Services, invocation.RunId,
            row => row.Disposition == (int)EtlDeliveryDisposition.Consumed, timeout.Token);
    }
}
