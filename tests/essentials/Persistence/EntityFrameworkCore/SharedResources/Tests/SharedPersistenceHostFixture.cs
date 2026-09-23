using Elsa.Workbench.Tests;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

/// <summary>Runs the built Workbench with named target connections and supports a clean process restart.</summary>
public sealed class SharedPersistenceHostFixture : IAsyncDisposable
{
    private readonly WorkbenchShell _shell;
    private WorkbenchProcess? _process;

    public SharedPersistenceHostFixture(
        PostgreSqlTargetFixture targets,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var combined = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Shared"] = targets.PrimaryConnectionString,
            ["ConnectionStrings:Diagnostics"] = targets.DiagnosticsConnectionString
        };

        if (settings is not null)
            foreach (var (key, value) in settings)
                combined[key] = value;

        _shell = WorkbenchShell.Development with { Settings = combined };
    }

    public WorkbenchProcess Process => _process ?? throw new InvalidOperationException("The Workbench has not started.");

    public async Task StartAsync()
    {
        if (_process is not null)
            throw new InvalidOperationException("The Workbench is already running.");

        _process = await WorkbenchProcess.StartAsync(_shell);
    }

    public async Task RestartAsync()
    {
        if (_process is null)
            throw new InvalidOperationException("The Workbench has not started.");

        var stopped = await _process.StopAsync(TimeSpan.FromSeconds(30));
        if (stopped is null)
            throw new TimeoutException("The Workbench did not stop cleanly.");

        await _process.DisposeAsync();
        _process = null;
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
            await _process.DisposeAsync();
    }
}
