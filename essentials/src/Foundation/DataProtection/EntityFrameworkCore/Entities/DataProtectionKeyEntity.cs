namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;

/// <summary>
/// One element of the shared key ring: a key, or a revocation, as the ASP.NET Core key manager wrote it. Rows are only ever
/// added; the key manager never rewrites or deletes one.
/// </summary>
public sealed class DataProtectionKeyEntity
{
    /// <summary>
    /// Chosen by the store as it adds the row, so no engine has to generate it. The key manager gives the order of the
    /// elements it reads back no meaning, so neither does the store.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>The name the key manager gave the element, such as <c>key-&lt;id&gt;</c>; optional, and never read back.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>
    /// The element itself. A key's secret is encrypted inside it when the host configures a certificate, and stored as
    /// written otherwise.
    /// </summary>
    public string Xml { get; set; } = null!;

    /// <summary>The persisted-schema version of this row: <see cref="DataProtectionKeysEfModule.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
}
