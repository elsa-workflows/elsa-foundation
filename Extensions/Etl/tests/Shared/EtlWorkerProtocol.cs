using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;

namespace Elsa.Etl.Tests.Support;

internal sealed record EtlWorkerConfiguration(string Root, EtlScope Scope, List<EtlHostFileBindingOptions> Bindings);

internal sealed record EtlWorkerAcceptance(EtlPartitionLease Lease, EtlBatchAcceptance Acceptance, int ProcessId);

internal static class EtlWorkerProtocol
{
    public const string ReadyPrefix = "ETL-WORKER-READY ";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static string ConfigurationPath(string root) => Path.Combine(root, "worker.json");

    public static string JournalDirectory(string root) => Path.Combine(root, "journal");

    public static string AcceptancePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.acceptance.json");
    }

    public static string ReleasePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.release");
    }

    public static void ValidateRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (role.Length > 32 || role.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("The ETL worker role must contain only ASCII letters, digits, hyphens, or underscores.", nameof(role));
    }

    public static async Task WriteConfigurationAsync(string root, EtlWorkerConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(root);
        await WriteAtomicallyAsync(ConfigurationPath(root), configuration, overwrite: true, cancellationToken);
    }

    public static async Task<EtlWorkerConfiguration> ReadConfigurationAsync(string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<EtlWorkerConfiguration>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The ETL worker configuration is empty.");
    }

    public static async Task WriteAcceptanceAsync(string root, string role, EtlWorkerAcceptance acceptance,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(acceptance);
        Directory.CreateDirectory(JournalDirectory(root));
        await WriteAtomicallyAsync(AcceptancePath(root, role), acceptance, overwrite: false, cancellationToken);
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.partial");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
