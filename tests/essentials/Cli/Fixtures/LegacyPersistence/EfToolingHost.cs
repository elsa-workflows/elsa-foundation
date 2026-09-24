namespace Elsa.Persistence.EntityFramework.Tooling;

public static class EfToolingHost
{
    public static async Task<int> RunAsync(Stream request, Stream response)
    {
        var marker = Environment.GetEnvironmentVariable("ELSA_LEGACY_TOOLING_MARKER");
        if (!string.IsNullOrWhiteSpace(marker))
            File.WriteAllText(marker, "v1 tooling invoked");

        await response.WriteAsync("""{"version":1,"status":"ok","exitCode":0,"command":"list","list":{"modules":[]}}"""u8.ToArray());
        return 0;
    }
}
