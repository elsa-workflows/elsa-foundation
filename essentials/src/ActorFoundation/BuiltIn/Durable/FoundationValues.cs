using System.Security.Cryptography;

namespace Kernel.Durable;

internal readonly record struct FoundationValueReference(string Sha256, int Length);

internal sealed class FoundationValue
{
    private readonly byte[] _bytes;

    internal FoundationValue(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes.ToArray();
        Reference = new(Convert.ToHexString(SHA256.HashData(_bytes)), _bytes.Length);
    }

    internal FoundationValueReference Reference { get; }
    internal byte[] Read() => (byte[])_bytes.Clone();
    internal bool Matches(FoundationValue other) => Reference == other.Reference && _bytes.AsSpan().SequenceEqual(other._bytes);
}

public sealed partial class InMemoryStore
{
    private readonly Dictionary<CreationAddress, Dictionary<string, FoundationValue>> _foundationValues = [];

    internal byte[]? ReadFoundationValue(CreationAddress actor, FoundationValueReference reference)
    {
        if (reference.Sha256 is not { Length: 64 } || reference.Length < 0) return null;
        lock (_gate)
        {
            if (Instances.GetValueOrDefault(actor.Id) is not { Retired: false } current || current.Creation != actor.Creation)
                return null;
            var value = _foundationValues.GetValueOrDefault(actor)?.GetValueOrDefault(reference.Sha256);
            return value is not null && value.Reference == reference ? value.Read() : null;
        }
    }

    internal int FoundationValueCount(CreationAddress actor)
    {
        lock (_gate) return _foundationValues.GetValueOrDefault(actor)?.Count ?? 0;
    }

    private void ValidateFoundationValues(TurnCommit turn)
    {
        if (turn.FoundationValues is not { Count: > 0 } submitted) return;
        var address = new CreationAddress(turn.Instance, turn.Row!.Creation);
        var stored = _foundationValues.GetValueOrDefault(address);
        var staged = new Dictionary<string, FoundationValue>(StringComparer.Ordinal);
        foreach (var value in submitted)
        {
            var digest = value.Reference.Sha256;
            var existing = stored?.GetValueOrDefault(digest) ?? staged.GetValueOrDefault(digest);
            if (existing is not null && !existing.Matches(value))
                throw new StoreException(Failure.Definite, "A stored digest must describe an immutable value.");
            staged.TryAdd(digest, value);
        }
    }

    private void ApplyFoundationValues(TurnCommit turn)
    {
        if (turn.FoundationValues is not { Count: > 0 } submitted) return;
        var address = new CreationAddress(turn.Instance, turn.Row!.Creation);
        if (!_foundationValues.TryGetValue(address, out var values))
            _foundationValues.Add(address, values = new(StringComparer.Ordinal));
        foreach (var value in submitted)
            values.TryAdd(value.Reference.Sha256, value);
    }
}
