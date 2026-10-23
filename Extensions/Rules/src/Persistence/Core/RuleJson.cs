using System.Buffers;
using System.Text.Json;

namespace Elsa.Rules.Persistence.Core;

/// <summary>Canonical JSON writes stop at the representation limit, before retaining oversized output.</summary>
internal static class RuleJson
{
    public static byte[] Write(Action<Utf8JsonWriter> write, int maximumBytes)
    {
        var buffer = new BoundedBuffer(maximumBytes);
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenBytes.ToArray();
    }

    private sealed class BoundedBuffer(int maximumBytes) : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public ReadOnlySpan<byte> WrittenBytes => _buffer.WrittenSpan;

        public void Advance(int count)
        {
            if (count < 0 || (long)_buffer.WrittenCount + count > maximumBytes)
                throw new InvalidDataException("rules.representation-too-large");
            _buffer.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _buffer.GetMemory(sizeHint);
        public Span<byte> GetSpan(int sizeHint = 0) => _buffer.GetSpan(sizeHint);
    }
}
