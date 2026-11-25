using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>Selects the JSON conversion contract of a user type or an activity input member. Nothing is stamped into executables.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class JsonConversionAttribute : Attribute
{
    /// <summary>Gets or sets the member naming convention. Unspecified uses CLR names.</summary>
    public JsonKnownNamingPolicy NamingPolicy { get; set; } = JsonKnownNamingPolicy.CamelCase;
    /// <summary>Gets or sets whether matching at this conversion boundary ignores case.</summary>
    public bool CaseInsensitive { get; set; }
    /// <summary>Gets or sets whether unmatched members refuse conversion instead of warning.</summary>
    public bool RefuseUnmatchedMembers { get; set; }
    /// <summary>Gets or sets explicit mappings, each written as JSON-name=CLR-member-name.</summary>
    public string[] MemberMappings { get; set; } = [];
}
