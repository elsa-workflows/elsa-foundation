using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Composition-only qualification, resolved lazily so reading retained metadata does not activate an actor.</summary>
internal sealed class RuntimeHistorySourceAvailability(IServiceProvider services)
{
    internal bool Delivery()
    {
        try { return services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
            writer.CanRecordHistory(reader) && services.GetService<IBookmarkResumeDispatcher>() is IRuntimeHistoryDeliverySource source && source.CanRecordHistory(writer, reader); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return false; }
    }
    internal bool Timer()
    {
        try { return services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
            writer.CanRecordHistory(reader) && services.GetService<IDurableTimerStore>() is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } store &&
            services.GetService<IDurableTimerScheduler>() is IRuntimeHistoryTimerSource source && source.CanRecordHistory(store); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return false; }
    }
}
