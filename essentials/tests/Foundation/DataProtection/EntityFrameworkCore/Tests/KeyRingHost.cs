using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>
/// One host of a test, composed as <c>Elsa.Workbench</c> and <c>Elsa.Foundation.Host</c> compose Data Protection, from
/// configuration, and started. Each has a content root of its own, as two hosts deployed to two directories do, so the
/// application name ASP.NET Core would derive from it differs from every other host's.
/// </summary>
internal sealed class KeyRingHost : IAsyncDisposable
{
    public const string Purpose = "Elsa.Foundation.DataProtection.Tests";

    private readonly IHost _host;
    private readonly DirectoryInfo _contentRoot;

    private KeyRingHost(IHost host, DirectoryInfo contentRoot, CapturedLog log)
    {
        _host = host;
        _contentRoot = contentRoot;
        Log = log;
    }

    public IServiceProvider Services => _host.Services;

    public CapturedLog Log { get; }

    public string ContentRoot => _contentRoot.FullName;

    /// <summary>Builds a host from <paramref name="settings"/>, lets <paramref name="compose"/> add to its composition, and starts it.</summary>
    public static async Task<KeyRingHost> StartAsync(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection, IConfiguration>? compose = null)
    {
        var host = Build(settings, compose);
        await host._host.StartAsync();
        return host;
    }

    /// <summary>Builds a host without starting it; composing it may already throw.</summary>
    public static KeyRingHost Build(IReadOnlyDictionary<string, string?> settings, Action<IServiceCollection, IConfiguration>? compose = null)
    {
        var contentRoot = Directory.CreateTempSubdirectory("elsa-key-ring-host-");
        try
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = contentRoot.FullName });
            builder.Configuration.AddInMemoryCollection(settings);
            var log = new CapturedLog();
            builder.Logging.AddProvider(log);
            builder.Services.AddConfiguredDataProtection(builder.Configuration);
            compose?.Invoke(builder.Services, builder.Configuration);
            return new KeyRingHost(builder.Build(), contentRoot, log);
        }
        catch
        {
            contentRoot.Delete(recursive: true);
            throw;
        }
    }

    public Task StartAsync() => _host.StartAsync();

    public string Protect(string plaintext) => Protector.Protect(plaintext);

    public string Unprotect(string payload) => Protector.Unprotect(payload);

    /// <summary>The key ring's rows, read as the module reads them, in the database this host's key store reaches.</summary>
    public async Task<IReadOnlyList<DataProtectionKeyEntity>> StoredKeysAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().Keys.AsNoTracking().ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using (var host = (IAsyncDisposable)_host)
        {
            await _host.StopAsync();
        }

        _contentRoot.Delete(recursive: true);
    }

    private IDataProtector Protector => Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
}

/// <summary>Every entry the host logged, with its level and category, kept for assertion.</summary>
internal sealed class CapturedLog : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Category, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Category, string Message)> Entries
    {
        get
        {
            lock (_entries)
                return [.. _entries];
        }
    }

    public IEnumerable<string> At(LogLevel level) => Entries.Where(entry => entry.Level == level).Select(entry => $"{entry.Category}: {entry.Message}");

    /// <summary>The messages <paramref name="category"/> logged at <paramref name="level"/>.</summary>
    public IEnumerable<string> From(string category, LogLevel level) =>
        Entries.Where(entry => entry.Level == level && entry.Category == category).Select(entry => entry.Message);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (log._entries)
                log._entries.Add((logLevel, category, formatter(state, exception) + (exception is null ? "" : $" {exception.GetType().Name}: {exception.Message}")));
        }
    }
}
