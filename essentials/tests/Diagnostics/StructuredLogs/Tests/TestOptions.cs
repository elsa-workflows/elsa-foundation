using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

internal static class TestOptions
{
    public static IOptions<StructuredLogsOptions> Create(Action<StructuredLogsOptions>? configure = null)
    {
        var options = new StructuredLogsOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}
