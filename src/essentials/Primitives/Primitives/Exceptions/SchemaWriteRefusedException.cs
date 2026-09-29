namespace Elsa.Primitives.Exceptions;

/// <summary>
/// A write refused because its value needs a schema version later than the one this host may write for the family
/// (spec 180, FR-016a). Nothing was saved. Every first-party domain API answers it with HTTP 409 in its own problem
/// envelope, carrying <see cref="Code"/>, the family and both versions.
/// </summary>
/// <remarks>
/// <para>
/// The persistence that raises it and the API that answers it are separately loaded, and neither may depend on the
/// other: an API resolves no EF Core, and a store knows no HTTP. This base is what both see, which is why it lives
/// here. <c>Elsa.Persistence.EntityFramework</c>'s <c>EfSchemaWriteRefusedException</c> is the refusal a store raises.
/// </para>
/// <para>
/// It derives from <see cref="Exception"/> alone, so no catch filter or fault ladder that names
/// <see cref="InvalidOperationException"/>, <see cref="ArgumentException"/>, <see cref="FormatException"/>,
/// <see cref="NotSupportedException"/> or <c>JsonException</c> turns it into corruption or a 400.
/// </para>
/// </remarks>
public abstract class SchemaWriteRefusedException : Exception
{
    /// <summary>The stable code every problem envelope that reports the refusal carries.</summary>
    public const string RefusalCode = "schema-write-refused";

    protected SchemaWriteRefusedException(string family, string writeVersion, string requiredVersion, string message)
        : base(message)
    {
        Family = family;
        WriteVersion = writeVersion;
        RequiredVersion = requiredVersion;
    }

    /// <summary>The stable code: <see cref="RefusalCode"/>.</summary>
    public string Code => RefusalCode;

    /// <summary>The schema family the write belongs to.</summary>
    public string Family { get; }

    /// <summary>The version this host may write for the family.</summary>
    public string WriteVersion { get; }

    /// <summary>The version the value being written needs.</summary>
    public string RequiredVersion { get; }
}
