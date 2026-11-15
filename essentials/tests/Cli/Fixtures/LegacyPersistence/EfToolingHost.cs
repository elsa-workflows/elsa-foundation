using System.Text;

namespace Elsa.Persistence.EntityFramework.Tooling;

public static class EfToolingHost
{
    public static async Task<int> RunAsync(Stream request, Stream response)
    {
        using var reader = new StreamReader(request, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        var marker = Environment.GetEnvironmentVariable("ELSA_LEGACY_TOOLING_MARKER");
        if (!string.IsNullOrWhiteSpace(marker))
            File.WriteAllText(marker, text);

        // A build that predates the cluster: `status` answers each family's own state and nothing about the members.
        await response.WriteAsync(text.Contains("\"command\":\"status\"", StringComparison.Ordinal)
            ? """{"version":1,"status":"ok","exitCode":0,"command":"status","finalization":{"provider":"Sqlite","schema":null,"families":[]}}"""u8.ToArray()
            : """{"version":1,"status":"ok","exitCode":0,"command":"list","list":{"modules":[]}}"""u8.ToArray());
        return 0;
    }
}
