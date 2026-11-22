using CShells.Features;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workbench;

/// <summary>
/// Opts every shell into workflow-fault stack-trace capture. The server defaults to capturing (its historical
/// behavior); operators can turn it off — stack traces may leak internals in production — via the
/// <see cref="RuntimeFaultCaptureOptions.SectionName"/> configuration section (bound in Program.cs).
/// </summary>
/// <remarks>
/// Must be public: CShells feature discovery scans exported types only, so an internal feature class never
/// enters the runtime feature catalog and is silently dropped from every shell that requests it.
/// </remarks>
public sealed class RuntimeFaultStackTraceFeature : IShellFeature
{
    public bool CaptureStackTrace { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<RuntimeFaultCaptureOptions>(o => o.CaptureStackTrace = CaptureStackTrace);
    }
}
