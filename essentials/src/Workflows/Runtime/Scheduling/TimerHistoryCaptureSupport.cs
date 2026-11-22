using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Scheduling;

internal sealed class TimerHistoryCaptureSupport(IServiceProvider services) : IRuntimeHistoryTimerSupport
{
    public bool CanCapture => services.GetService<IDurableTimerStore>() is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } store &&
        services.GetService<IRuntimeHistoryStore>() is { } reader && store.CanInspectHistorySources(reader) &&
        services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && writer.CanRecordHistory(reader) &&
        services.GetService<IDurableTimerScheduler>()?.CanRecordHistory(writer, reader) == true &&
        services.GetService<IBookmarkResumeDispatcher>()?.CanRecordHistory(writer, reader) == true &&
        services.GetService<IPersistenceScopeRunner>() is { } scopes && services.GetService<TimeProvider>() is { } clock &&
        services.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>().Any(task => task.CanCaptureHistory(scopes, clock));
}
