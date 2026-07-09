using CShells.Features;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Server;

internal sealed class RuntimeFaultStackTraceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<RuntimeFaultCaptureOptions>(o => o.CaptureStackTrace = true);
    }
}
