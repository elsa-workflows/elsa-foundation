using System.Reflection;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>
/// #3441 self-review round 2 (P3-2): every protected payload column of an Agent table is declared to the schema family,
/// as content or integrity, so it is stamped on write and upcast on read like the rest. The shared declaration guard
/// recognizes only columns named exactly <c>Payload</c> (or ending in <c>Json</c>), so the Agent's other payload columns are
/// held here.
/// </summary>
public sealed class AgentEfSchemaDeclarationTests
{
    [Fact]
    public void Every_payload_column_of_an_agent_table_is_declared()
    {
        var assembly = typeof(WeaverDbContext).Assembly;
        var declared = assembly.GetCustomAttributes<EfSchemaContentAttribute>().SelectMany(x => x.Columns.Select(column => (x.Entity, column)))
            .Concat(assembly.GetCustomAttributes<EfSchemaIntegrityAttribute>().Select(x => (x.Entity, x.Column)))
            .ToHashSet();
        var payloads = assembly.GetTypes().Where(type => type.Namespace == "Elsa.Weaver.Persistence.EntityFrameworkCore.Entities")
            .SelectMany(type => type.GetProperties().Where(property => property.PropertyType == typeof(string)
                && (property.Name.EndsWith("Payload", StringComparison.Ordinal) || property.Name == "Canonical"))
                .Select(property => (type, property.Name)))
            .ToArray();

        Assert.NotEmpty(payloads);
        Assert.Empty(payloads.Where(x => !declared.Contains(x)).Select(x => $"{x.type.Name}.{x.Name}"));
    }
}
