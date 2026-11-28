using Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>Uses the Foundation cursor format in nonpackable projection tests.</summary>
public sealed class FoundationReadCursorFixture(byte[] key)
{
    private readonly FoundationReadCursor _cursor = new(key);

    public string Seal(ReadOnlySpan<byte> context, ReadOnlySpan<byte> plain) => _cursor.Seal(context, plain);

    public bool TryOpen(ReadOnlySpan<byte> context, string? token, Span<byte> plain) =>
        _cursor.TryOpen(context, token, plain);
}
