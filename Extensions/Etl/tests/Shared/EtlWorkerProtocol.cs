using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;

namespace Elsa.Etl.Tests.Support;

internal enum EtlWorkerWritePause { None, SourceSnapshot, OutputChunk }
internal enum EtlWorkerStoreGate { BeforeAcceptance, BeforeReservation, AfterAcceptanceCommit, AfterDeliveryClaim }

internal sealed record EtlWorkerConfiguration(string Root, EtlScope Scope, List<EtlHostFileBindingOptions> Bindings,
    EtlWorkerWritePause WritePause = EtlWorkerWritePause.None,
    EtlWorkerStoreGate StoreGate = EtlWorkerStoreGate.BeforeAcceptance);

internal sealed record EtlWorkerAcceptance(EtlPartitionLease Lease, EtlBatchAcceptance Acceptance, int ProcessId);
internal sealed record EtlWorkerCommittedAcceptance(EtlPartitionLease Lease, EtlBatchAcceptance Acceptance,
    EtlAcceptanceResult Result, int ProcessId);
internal sealed record EtlWorkerDelivery(EtlDeliveryLease Lease, int ProcessId);
internal sealed record EtlWorkerReservation(EtlPartitionLease Lease, EtlBatchDescriptor Batch, int ProcessId);

internal sealed record EtlWorkerWrite(string OutputRoot, string EffectIdentity, string Extension,
    int PrefixBytes, int WriteBytes, int ProcessId);

internal static class EtlWorkerProtocol
{
    public const string ReadyPrefix = "ETL-WORKER-READY ";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static string ConfigurationPath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.configuration.json");
    }

    public static string JournalDirectory(string root) => Path.Combine(root, "journal");

    public static string AcceptancePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.acceptance.json");
    }

    public static string CommittedAcceptancePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.committed-acceptance.json");
    }

    public static string CommittedAcceptanceReceiptPath(string root, string role, int ordinal)
    {
        ValidateRole(role);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinal);
        return Path.Combine(JournalDirectory(root), $"{role}.committed-acceptance-{ordinal:D4}.json");
    }

    public static string DeliveryPath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.delivery.json");
    }

    public static string ReservationPath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.reservation.json");
    }

    public static string ReleasePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.release");
    }

    public static string WritePath(string root, string role)
    {
        ValidateRole(role);
        return Path.Combine(JournalDirectory(root), $"{role}.write.json");
    }

    public static void ValidateRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (role.Length > 32 || role.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("The ETL worker role must contain only ASCII letters, digits, hyphens, or underscores.", nameof(role));
    }

    public static async Task WriteConfigurationAsync(string root, string role, EtlWorkerConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(root);
        await WriteAtomicallyAsync(ConfigurationPath(root, role), configuration, overwrite: false, cancellationToken);
    }

    public static async Task<EtlWorkerConfiguration> ReadConfigurationAsync(string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<EtlWorkerConfiguration>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The ETL worker configuration is empty.");
    }

    public static Task WriteAcceptanceAsync(string root, string role, EtlWorkerAcceptance acceptance,
        CancellationToken cancellationToken = default) =>
        WriteJournalAsync(root, AcceptancePath(root, role), acceptance, cancellationToken);

    public static Task WriteCommittedAcceptanceAsync(string root, string role, EtlWorkerCommittedAcceptance acceptance,
        CancellationToken cancellationToken = default) =>
        WriteJournalAsync(root, CommittedAcceptancePath(root, role), acceptance, cancellationToken);

    public static Task WriteCommittedAcceptanceReceiptAsync(string root, string role, int ordinal,
        EtlWorkerCommittedAcceptance acceptance, CancellationToken cancellationToken = default) =>
        WriteJournalAsync(root, CommittedAcceptanceReceiptPath(root, role, ordinal), acceptance, cancellationToken);

    public static Task WriteDeliveryAsync(string root, string role, EtlWorkerDelivery delivery,
        CancellationToken cancellationToken = default) =>
        WriteJournalAsync(root, DeliveryPath(root, role), delivery, cancellationToken);

    public static Task WriteReservationAsync(string root, string role, EtlWorkerReservation reservation,
        CancellationToken cancellationToken = default) =>
        WriteJournalAsync(root, ReservationPath(root, role), reservation, cancellationToken);

    public static Task WriteStagingAsync(string root, string role, EtlWorkerWrite write, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(WritePath(root, role), write, overwrite: false, cancellationToken);

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

    private static Task WriteJournalAsync<T>(string root, string path, T value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(JournalDirectory(root));
        return WriteAtomicallyAsync(path, value, overwrite: false, cancellationToken);
    }
}
