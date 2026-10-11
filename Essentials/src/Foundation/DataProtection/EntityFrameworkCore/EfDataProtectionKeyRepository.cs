using System.Xml.Linq;
using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

/// <summary>
/// The key ring's elements in the platform database (#2191), so every host that reaches the same table protects and
/// unprotects with the same keys, and a recreated host finds the keys it had.
/// </summary>
/// <remarks>
/// <para>
/// It is the host's one instance: built in the host's own container, from whose scopes it resolves the module's context, so it
/// reads the connection the host configured and writes through the gate the host's migrator admitted. Every shell container
/// CShells builds from copies of the host's registrations resolves this same instance, never a copy of its own.
/// </para>
/// <para>
/// The key manager's contract is synchronous, so the store is too. It runs only when the key ring is created or refreshed, at
/// most once per refresh period, never once per protected payload.
/// </para>
/// <para>
/// A row stamped with a version this build cannot read fails the read rather than being skipped: a key ring that silently
/// dropped keys would answer every payload they protected with "the key was not found", which is the failure that looks like
/// an expired session.
/// </para>
/// </remarks>
public sealed class EfDataProtectionKeyRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>();
        return context.Keys
            .AsNoTracking()
            .AsEnumerable()
            .Select(row => XElement.Parse(DataProtectionKeysEfModule.Chain.Upcast<DataProtectionKeyEntity>(
                row.SchemaVersion, (nameof(row.Xml), row.Xml))[nameof(row.Xml)]!))
            .ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>();
        context.Keys.Add(new DataProtectionKeyEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            SchemaVersion = DataProtectionKeysEfModule.SchemaVersion
        });
        context.SaveChanges();
    }
}
