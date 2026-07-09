using CShells.Features;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Server;

internal sealed class RuntimeFaultStackTraceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton(new RuntimeFaultCaptureOptions
        {
            CaptureStackTrace = true
        }));
    }
}
