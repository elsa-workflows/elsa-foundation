namespace Elsa.Persistence.EntityFramework.Tooling;

public sealed class EfToolingConfigurationContext : IDisposable
{
    public const int Version = 1;

    public void Dispose() => Interlocked.Increment(ref Elsa.Cli.Fixtures.WorkerContextHost.FakeContextHost.ContextsDisposed);
}

public static class EfToolingContextContract
{
    public const int Version = 2;
}
