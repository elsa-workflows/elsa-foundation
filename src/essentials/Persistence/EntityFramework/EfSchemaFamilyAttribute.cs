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
/// names an <see cref="EfModuleAttribute"/> that same assembly declares - or, for a family shared by no single module
/// (see below), it sits in an assembly with no <see cref="EfModuleAttribute"/> of its own, and <see cref="Module"/> is
/// <see langword="null"/>.
/// </para>
/// <para>
/// Spec 180's upcaster chain (B4, #2100) joins this declaration. Until it does, a build reads only its
/// <see cref="CurrentVersion"/>, which is what <see cref="EfSchemaFamilyDescriptor.ReadableVersions"/> reports.
/// </para>
/// <para>
/// A family owned by shared mapping code that several EF modules' contexts call into - <c>modelBuilder.MapXyz(...)</c>
/// from a package with no <see cref="EfModuleAttribute"/> of its own, one version defined once - has no single owning
/// module to name. The two-argument constructor declares exactly that: <see cref="Module"/> reads
/// <see langword="null"/>, and <see cref="EfSchemaFamilyCatalog"/> accepts it only in an assembly that declares no
/// <see cref="EfModuleAttribute"/> at all, so a module's own family still names its module explicitly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfSchemaFamilyAttribute : Attribute
{
    public EfSchemaFamilyAttribute(string name, string module, string currentVersion)
    {
        Name = name;
        Module = module;
        CurrentVersion = currentVersion;
    }

    /// <summary>Declares <paramref name="name"/> shared: owned by no single EF module. <see cref="Module"/> reads
    /// <see langword="null"/>.</summary>
    public EfSchemaFamilyAttribute(string name, string currentVersion)
    {
        Name = name;
        Module = null;
        CurrentVersion = currentVersion;
    }

    /// <summary>The family's identity, as its skew check names it: the module class's <c>SchemaFamily</c> constant.</summary>
    public string Name { get; }

    /// <summary>
    /// The canonical <see cref="EfModuleAttribute.Name"/> of the EF module that owns the family, or
    /// <see langword="null"/> for a family the two-argument constructor declared shared: owned by no single EF module.
    /// </summary>
    public string? Module { get; }

    /// <summary>
    /// The version this build stamps on the family's rows and compares their stamps against: the same constant its
    /// skew check passes to <see cref="EfSchemaVersion"/>.
    /// </summary>
    public string CurrentVersion { get; }
}
