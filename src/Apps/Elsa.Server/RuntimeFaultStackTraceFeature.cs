using CShells.Features;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Server;

/// <summary>
/// Opts every shell into workflow-fault stack-trace capture. The server defaults to capturing (its historical
/// behavior); operators can turn it off — stack traces may leak internals in production — via the
/// <see cref="RuntimeFaultCaptureOptions.SectionName"/> configuration section (bound in Program.cs).
/// </summary>
internal sealed class RuntimeFaultStackTraceFeature : IShellFeature
{
    public bool CaptureStackTrace { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton(new RuntimeFaultCaptureOptions
        {
            CaptureStackTrace = CaptureStackTrace
        }));
    }
}
