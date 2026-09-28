namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one declaration of a schema family (spec 180, FR-001): the tables whose rows stamp one persisted-schema version,
/// the EF module that owns them, and the version this build stamps and reads. <see cref="EfSchemaFamilyCatalog.Discover"/>
/// is the one place that reads it, and a host's readability report (spec 183, FR-020) is derived from it alone.
/// </summary>
/// <remarks>
/// <para>
/// Assembly-level and constant-argument-only, as <see cref="EfModuleAttribute"/> is (ADR 0076 D2), so it is read as
/// metadata without composing a shell or a container. It sits in the owning module's assembly, and <see cref="Module"/>
/// names an <see cref="EfModuleAttribute"/> that same assembly declares.
/// </para>
/// <para>
/// Spec 180's upcaster chain (B4, #2100) joins this declaration. Until it does, a build reads only its
/// <see cref="CurrentVersion"/>, which is what <see cref="EfSchemaFamilyDescriptor.ReadableVersions"/> reports.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfSchemaFamilyAttribute(string name, string module, string currentVersion) : Attribute
{
    /// <summary>The family's identity, as its skew check names it: the module class's <c>SchemaFamily</c> constant.</summary>
    public string Name { get; } = name;

    /// <summary>The canonical <see cref="EfModuleAttribute.Name"/> of the EF module that owns the family.</summary>
    public string Module { get; } = module;

    /// <summary>
    /// The version this build stamps on the family's rows and compares their stamps against: the same constant its
    /// skew check passes to <see cref="EfSchemaVersion"/>.
    /// </summary>
    public string CurrentVersion { get; } = currentVersion;
}
