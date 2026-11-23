using Kernel.Durable;

namespace Kernel.Foundation.InMemory;

// A qualification seam for generated roots. The public model contract and lifetime rules are separate work.
internal interface IRootValueTurn
{
    FoundationValueReference StoreValue(ReadOnlySpan<byte> payload);
    ValueTask<byte[]?> ReadValueAsync(FoundationValueReference reference, CancellationToken cancellationToken = default);
}

internal delegate ValueTask<byte[]?> RootValueReader(FoundationDispatchHeader header, CreationAddress actor,
    FoundationValueReference reference, CancellationToken cancellationToken);
