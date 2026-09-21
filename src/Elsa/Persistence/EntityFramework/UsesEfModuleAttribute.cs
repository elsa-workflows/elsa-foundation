namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Declares that a shell feature class depends on an EF module (spec 171 FR-064): the module whose
/// migrations it registers with <c>AddEfModuleMigrations&lt;TContext&gt;</c>, or whose
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> it reads without registering one of its own. It is
/// the only mapping from a CShells feature name to a canonical <see cref="EfModuleAttribute.Name"/>, and
/// both the CLI's provider-agreement check and the feature-activation guard read it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AllowMultiple"/> because a feature can depend on more than one module:
/// <c>WorkflowsDashboardEntityFrameworkCoreFeature</c> reads both <c>RuntimeDbContext</c> and
/// <c>WorkflowsDesignDbContext</c> while registering migrations for neither. The attribute is carried on the
/// feature class wherever that class lives — <c>AspNetCoreIdentityEntityFrameworkCoreFeature</c> sits in a
/// different project from <c>Identity.Iam</c>'s own feature and still registers that module's migrations —
/// so a module's feature set is never assumed to be its own project's.
/// </para>
/// <para>
/// Constant-argument-only and type-free, the same discipline <see cref="EfModuleAttribute"/> keeps (ADR
/// 0076 D2): it names the module by its canonical string, so it can be read as metadata without loading the
/// module's context type, which is what a package loader deciding whether a module may activate needs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UsesEfModuleAttribute(string module) : Attribute
{
    /// <summary>The canonical <see cref="EfModuleAttribute.Name"/> of the module this feature depends on.</summary>
    public string Module { get; } = module;
}
