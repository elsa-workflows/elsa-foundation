using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

/// <summary>
/// The EF key store's settings (#2191): those of every host-composed EF store, with <c>ConnectionStrings:Elsa</c> as the
/// connection a store that names none uses, so the keys live with the platform's data.
/// </summary>
public sealed class EfDataProtectionKeyStoreOptions : EfHostStoreOptions
{
    /// <summary>The subsection of <see cref="DataProtectionConfigurationExtensions.SectionName"/> these are read from.</summary>
    public const string SectionKey = "EntityFrameworkCore";
}
