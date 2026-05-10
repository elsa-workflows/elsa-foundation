using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Tasks.Core
{
    public interface IScheduledTaskExecution : IDisposable, IAsyncDisposable
    {
    }
}
