using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Tasks.Core
{
    /// <summary>
    /// Represents a recurring task that is executed in the background.
    /// In multi-tenant applications, this task is executed for each tenant.
    /// </summary>
    public interface IRecurringTask : ITask
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);

        ITaskSchedule GetSchedule();
    }
}
