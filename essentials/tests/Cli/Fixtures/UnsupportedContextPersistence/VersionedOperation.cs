namespace Elsa.Persistence.EntityFramework.Tooling;

public static class EfToolingContextContract
{
    public const int Version = 99;
}

public static partial class EfToolingHost
{
    public static Task<int> RunAsync(
        Stream request,
        Stream response,
        EfToolingConfigurationContext context,
        CancellationToken cancellationToken)
    {
        WriteMarker("ELSA_CONTEXT_OPERATION_MARKER");
        return Task.FromResult(0);
    }
}
