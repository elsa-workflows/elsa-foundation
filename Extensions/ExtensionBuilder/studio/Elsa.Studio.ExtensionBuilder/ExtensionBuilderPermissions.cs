namespace Elsa.Studio.ExtensionBuilder;

/// <summary>
/// The host-control permission keys that gate the Extension Builder bridge (#249, #256, ADR 0037). The string values
/// mirror the backend's Extension Builder permission contribution; Studio only needs the keys to check the user's
/// permission claims. Implication is expanded locally: <see cref="ReadKeys"/> lists both <c>read</c> and <c>manage</c>,
/// so a <c>manage</c>-only holder satisfies a read surface without Studio relying on the backend to expand it.
/// </summary>
internal static class ExtensionBuilderPermissions
{
    public const string Read = "extension-builder.read";
    public const string Manage = "extension-builder.manage";

    /// <summary>Satisfies a READ surface: the read permission, or manage (which implies read).</summary>
    public static readonly string[] ReadKeys = [Read, Manage];

    /// <summary>Satisfies a MUTATION surface: manage only. A mere authenticated session is not enough.</summary>
    public static readonly string[] ManageKeys = [Manage];
}
