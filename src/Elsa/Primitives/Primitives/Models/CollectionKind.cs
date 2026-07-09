using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models;

/// <summary>
/// The collection shape applied to an authored argument's element type. A missing/unset value is
/// treated as <see cref="Single"/> (FR-002).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CollectionKind>))]
public enum CollectionKind
{
    /// <summary>A scalar value; closes to <c>T</c>.</summary>
    Single,

    /// <summary>An array; closes to <c>T[]</c>.</summary>
    Array,

    /// <summary>A generic list; closes to <c>List&lt;T&gt;</c>.</summary>
    List,

    /// <summary>A generic set (default equality); closes to <c>HashSet&lt;T&gt;</c>.</summary>
    HashSet
}
