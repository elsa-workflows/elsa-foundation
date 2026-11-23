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

    /// <summary>Authenticates a JSON value canonically, retaining the supplied UTF-8 bytes as the payload.</summary>
    string EncodeJson(string purpose, ReadOnlySpan<byte> payload);

    /// <summary>Authenticates the JSON domain and returns the original UTF-8 payload.</summary>
    byte[] DecodeJson(string purpose, string token);
}
