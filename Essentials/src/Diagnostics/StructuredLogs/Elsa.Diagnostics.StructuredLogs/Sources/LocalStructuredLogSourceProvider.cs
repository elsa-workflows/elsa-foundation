using System.Diagnostics;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Sources;

/// <summary>
/// Exposes the single local host as the only known log source. The shape allows future remote sources to
/// be added without changing the contract.
/// </summary>
public sealed class LocalStructuredLogSourceProvider : IStructuredLogSourceProvider
{
    private readonly LogSource _localSource;

    public LocalStructuredLogSourceProvider(string? serviceName = null, string? displayName = null)
    {
        var machineName = Environment.MachineName;
        var process = Process.GetCurrentProcess();
        var resolvedServiceName = string.IsNullOrWhiteSpace(serviceName)
            ? process.ProcessName
            : serviceName;
        var id = resolvedServiceName.ToLowerInvariant();

        _localSource = new LogSource
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? resolvedServiceName : displayName,
            ServiceName = resolvedServiceName,
            MachineName = machineName,
            ProcessId = process.Id
        };
    }

    /// <summary>The id stamped onto every entry captured by this host.</summary>
    public string LocalSourceId => _localSource.Id;

    /// <inheritdoc />
    public LogSource GetLocalSource() => _localSource;

    /// <inheritdoc />
    public IReadOnlyList<LogSource> GetKnownSources() => [_localSource];
}
