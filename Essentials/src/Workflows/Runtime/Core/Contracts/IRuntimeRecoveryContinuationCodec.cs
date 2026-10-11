namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Protects provider-owned recovery continuation payloads before they cross a public boundary.
/// </summary>
/// <remarks>
/// Implementations must authenticate both the purpose and payload. Recovery scanners use the purpose to keep
/// continuations from one scanner or token format from being replayed against another scanner.
/// </remarks>
public interface IRuntimeRecoveryContinuationCodec
{
    string Encode(string purpose, ReadOnlySpan<byte> payload);

    byte[] Decode(string purpose, string token);
}
