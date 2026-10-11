namespace Elsa.Primitives.Exceptions;

/// <summary>
/// A write refused because its value needs a schema version later than the one this host may write for the family
/// (spec 180, FR-016a). Nothing was saved. Every first-party domain API answers it with HTTP 409 in its own problem
/// envelope, carrying <see cref="Code"/>, the family and both versions. <see cref="Code"/> is <see cref="RefusalCode"/>
/// for a store-level refusal; a derived refusal that owns a stable code of its own, such as spec 182's dormancy
/// refusal (Q17), carries that code instead.
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
    /// <summary>The stable code a store-level refusal's problem envelope carries.</summary>
    public const string RefusalCode = "schema-write-refused";

    protected SchemaWriteRefusedException(string family, string writeVersion, string requiredVersion, string message)
        : this(family, writeVersion, requiredVersion, message, RefusalCode)
    {
    }

    /// <summary>For a derived refusal that owns a stable code of its own, such as spec 182's dormancy refusal (Q17).</summary>
    protected SchemaWriteRefusedException(string family, string writeVersion, string requiredVersion, string message, string code)
        : base(message)
    {
        Family = family;
        WriteVersion = writeVersion;
        RequiredVersion = requiredVersion;
        Code = code;
    }

    /// <summary>The stable code every problem envelope that reports the refusal carries: <see cref="RefusalCode"/> unless
    /// the concrete refusal owns its own (spec 182, Q17).</summary>
    public string Code { get; }

    /// <summary>The schema family the write belongs to.</summary>
    public string Family { get; }

    /// <summary>The version this host may write for the family.</summary>
    public string WriteVersion { get; }

    /// <summary>The version the value being written needs.</summary>
    public string RequiredVersion { get; }
}
