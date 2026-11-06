using System.Buffers;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Bounds exact JSON bytes while allowing the framework writer's bounded worst-case escaping reservations.</summary>
internal sealed class BoundedRuntimeJsonBuffer(int maximum, string code = "runtime_object_limit") : IBufferWriter<byte>
{
    private byte[] _buffer = [];
    private int _written;
    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _written);
    public int Count => _written;
    public void Advance(int count)
    {
        if (count < 0 || count > maximum - _written || count > _buffer.Length - _written)
            throw ReleaseRuntimeArtifactCodec.Refuse(code);
        _written += count;
    }
    public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsMemory(_written); }
    public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return _buffer.AsSpan(_written); }
    private void Ensure(int sizeHint)
    {
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        sizeHint = Math.Max(sizeHint, 1);
        // Utf8JsonWriter requests minimum/padding buffers before it knows the final bytes advanced.
        // A legal string may reserve six bytes per UTF-16 code unit before writing fewer bytes.
        // The caller charges escaping before encode; Advance remains the exact advertised output bound.
        var capacityLimit = Math.Min(int.MaxValue, (long)maximum * 6 + 4096);
        var required = (long)_written + sizeHint;
        if (required > capacityLimit) throw ReleaseRuntimeArtifactCodec.Refuse(code);
        if (required <= _buffer.Length) return;
        var capacity = (int)Math.Min(capacityLimit, Math.Max(required, Math.Max(256L, (long)_buffer.Length * 2)));
        Array.Resize(ref _buffer, capacity);
    }
}
