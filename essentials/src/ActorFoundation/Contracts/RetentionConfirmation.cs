namespace Kernel.Foundation.Contracts;

/// <summary>Protects a retention quote for the same tenant and installed store across host restarts.</summary>
public interface IActorRetentionConfirmation
{
    ValueTask<string> ProtectRetentionQuoteAsync(BoundScope scope, ReadOnlyMemory<byte> quote,
        CancellationToken cancellationToken = default);

    ValueTask<byte[]?> UnprotectRetentionQuoteAsync(BoundScope scope, string confirmation,
        CancellationToken cancellationToken = default);
}
