using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHost;

internal static class KernelPersistenceMode
{
    internal static PersistenceMode Resolve(IServiceProvider services, WorkflowExecutable executable) =>
        services.GetService<IRuntimePersistenceModeResolver>()?.Resolve(executable) ??
        executable.PersistenceMode ??
        services.GetRequiredService<PersistenceModeOptions>().Mode;
}
