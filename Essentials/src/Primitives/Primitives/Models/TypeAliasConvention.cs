namespace Elsa.Primitives.Models;

/// <summary>
/// The single, shared alias convention applied identically by the runtime resolution registry
/// (<c>Elsa.Serialization.SystemText.WellKnownTypeRegistry</c>) and the reflection-only activity scanner
/// (<c>Elsa.Activities.Design.Reconciliation.Clr.ClrAssemblyScanner</c>). It is the one place that decides
/// "what stored token represents this CLR type" so the two call sites cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// A CLR type name is NEVER stored as a type identity. The convention yields:
/// </para>
/// <list type="bullet">
///   <item>For framework primitives / common BCL value types: the reserved bare BCL alias (<c>String</c>,
///   <c>Int32</c>, <c>Boolean</c>, <c>DateTime</c>, <c>Guid</c>, <c>Object</c>, …).</item>
///   <item>For every other type: its <see cref="Type.FullName"/> (dotted, namespace-qualified) with NO
///   assembly name and NO version. (<see cref="Type.Name"/> is used only in the degenerate case where
///   <see cref="Type.FullName"/> is null, e.g. open generic parameters.)</item>
/// </list>
/// <para>
/// Reflection-only safety: every decision is made by matching <see cref="Type.FullName"/> against a static
/// map — there are NO <c>typeof()</c> identity comparisons — so the convention works unchanged on types loaded
/// in a <see cref="System.Reflection.MetadataLoadContext"/>.
/// </para>
/// </remarks>
public static class TypeAliasConvention
{
    /// <summary>
    /// The canonical <c>{FullName → bare alias}</c> map for framework primitives and common BCL value types.
    /// This is the single source of truth for the reserved bare-alias set; the runtime registry derives its
    /// reserved-alias guard set from <see cref="ReservedBareAliases"/> below.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PrimitiveAliasesByFullName = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Reference primitives.
        ["System.String"] = "String",
        ["System.Object"] = "Object",
        // Integral types.
        ["System.Byte"] = "Byte",
        ["System.SByte"] = "SByte",
        ["System.Int16"] = "Int16",
        ["System.UInt16"] = "UInt16",
        ["System.Int32"] = "Int32",
        ["System.UInt32"] = "UInt32",
        ["System.Int64"] = "Int64",
        ["System.UInt64"] = "UInt64",
        ["System.Char"] = "Char",
        // Floating point / numeric.
        ["System.Single"] = "Single",
        ["System.Double"] = "Double",
        ["System.Decimal"] = "Decimal",
        // Logical.
        ["System.Boolean"] = "Boolean",
        // Common framework value types.
        ["System.DateTime"] = "DateTime",
        ["System.DateTimeOffset"] = "DateTimeOffset",
        ["System.TimeSpan"] = "TimeSpan",
        ["System.Guid"] = "Guid"
    };

    /// <summary>
    /// The canonical set of bare (non-dotted) aliases the framework owns (the BCL primitive aliases).
    /// Module-contributed types must use dotted aliases. Single-sourced from
    /// <see cref="PrimitiveAliasesByFullName"/> so the reserved set and the primitive map cannot diverge.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedBareAliases =
        new HashSet<string>(PrimitiveAliasesByFullName.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The names of the dynamic kind: any valid JSON value. <c>Any</c> is its alias; <c>Object</c>,
    /// <c>System.Object</c> and <c>Elsa.Any</c> are older names for it, and <c>JsonElement</c> and <c>JsonNode</c>
    /// are the .NET types an activity declares to hold it. <c>JsonObject</c> and <c>JsonArray</c> are not in the set:
    /// they hold JSON of one shape only.
    /// </summary>
    private static readonly IReadOnlySet<string> DynamicKindNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Any",
        "Elsa.Any",
        "Object",
        "System.Object",
        "JsonElement",
        "System.Text.Json.JsonElement",
        "JsonNode",
        "System.Text.Json.Nodes.JsonNode"
    };

    /// <summary>
    /// Whether <paramref name="alias"/> names the dynamic kind (spec 1228 FR-021): <c>Any</c>, one of its older names,
    /// or <c>JsonElement</c>/<c>JsonNode</c> by their short or full .NET names, nullable or not. Publication treats all
    /// of them as one kind when it compares a declared type with an input's. <c>JsonObject</c> and <c>JsonArray</c> are
    /// not the dynamic kind: they accept it and check the shape at run time.
    /// </summary>
    public static bool IsDynamicKind(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return false;

        var name = alias.Trim();
        if (name.EndsWith('?'))
            name = name[..^1];

        return DynamicKindNames.Contains(name);
    }

    /// <summary>
    /// Whether <paramref name="alias"/> names a JSON value of one shape: <c>JsonObject</c> or <c>JsonArray</c>, by short
    /// or full .NET name, nullable or not. Such an input accepts the dynamic kind at publish and checks the shape at
    /// run time (spec 1228 FR-021).
    /// </summary>
    public static bool IsShapedJson(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return false;

        var name = alias.Trim();
        if (name.EndsWith('?'))
            name = name[..^1];

        return name is "JsonObject" or "JsonArray" or "System.Text.Json.Nodes.JsonObject" or "System.Text.Json.Nodes.JsonArray";
    }

    /// <summary>
    /// Returns the canonical alias for <paramref name="type"/>: the reserved bare alias for a known
    /// primitive/common value type, otherwise the namespace-qualified <see cref="Type.FullName"/>
    /// (no assembly name, no version). Reflection-only safe.
    /// </summary>
    public static string CanonicalAlias(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
            return $"{CanonicalAlias(type.GetGenericArguments()[0])}?";

        var fullName = type.FullName;

        if (fullName is not null && PrimitiveAliasesByFullName.TryGetValue(fullName, out var bareAlias))
            return bareAlias;

        return fullName ?? type.Name;
    }
}
