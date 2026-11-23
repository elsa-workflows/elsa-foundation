namespace Elsa.Tasks.Tests;

/// <summary>
/// Running a startup task emits process-wide telemetry, which <see cref="StartupTaskTelemetryTests"/> captures and
/// counts. Every test class that runs startup tasks joins this collection, so none runs alongside those captures.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StartupTaskRuns
{
    public const string Name = "Startup task runs";
}
