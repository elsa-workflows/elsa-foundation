using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Extensions;

public static class EntityTypeBuilderExtensions
{
    public static void ConfigureTypeInformation<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TypeInformation?>> pathToTypeInfo)
        where TEntity : Entity
    {
        builder.OwnsOne(
            pathToTypeInfo,
            typeInfo =>
            {
                typeInfo.Property(t => t.AssemblyVersion);
                typeInfo.Property(t => t.AssemblyName);
                typeInfo.Property(t => t.Namespace);
                typeInfo.Property(t => t.TypeName);
            }
        );
    }
}
