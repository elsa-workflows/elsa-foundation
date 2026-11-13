# Dashboard definition persistence

`StudioDashboardEntityFrameworkCore` depends on native Dashboard Management. It supplies one scoped `IDashboardResourceStore` through `EfDashboardResourceStore<TContext>`; duplicate stores are rejected by public registration. See the [Core contracts](../../Core/EXTENSION_POINTS.md) and [management catalog](../../Management/EXTENSION_POINTS.md).

`AddDashboardResourcesEntityFrameworkCore<TContext>` supports an ordinary consumer DbContext; the consumer owns its model, provider and migrations and calls `AddDashboardResourcesModel`. Resource operations create independent contexts through the consumer's normal registration so they do not flush or clear unrelated tracked work.

The built-in feature composes only SQLite in this slice, using `DashboardResourcesSqliteDbContext`, migration history `ElsaStudioDashboard`, and schema family `StudioDashboard`1.0.0. It refuses built-in SqlServer/PostgreSql/MySql selection until those migrations are present. The package introduces no events, contributors or background tasks. See [persistence behavior and limits](README.md).
