using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// How a module's migrations assembly must be bound (spec 183, FR-021, amended 2026-09-29): by the assembly itself, never
/// by a name as well. EF Core resolves a name before it looks at an assembly, and from its own load context, which after an
/// in-place upgrade still holds the previous release under that name: the new release's context would find no migration
/// of its own, and validation would pass over every one it has pending. Each test project that checks a module's binding
/// compiles this file in.
/// </summary>
internal static class MigrationsAssemblyAssert
{
    public static void BoundByTheAssemblyItself(Assembly expected, RelationalOptionsExtension relational)
    {
        Assert.Same(expected, relational.MigrationsAssemblyObject);
        Assert.Null(relational.MigrationsAssembly);
    }
}
