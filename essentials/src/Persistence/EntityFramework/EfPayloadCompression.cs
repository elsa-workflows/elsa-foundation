namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Which codec a module writes its payload columns with. Reading never depends on this value: every frame
/// names its own codec, so a row written under one setting stays readable under any other.
/// </summary>
public enum EfPayloadCompression
{
    /// <summary>Write plaintext. The default, and what every module ships with.</summary>
    None = 0,

    /// <summary>Write a GZip frame when the frame is shorter than the plaintext, and plaintext otherwise.</summary>
    GZip = 1
}
