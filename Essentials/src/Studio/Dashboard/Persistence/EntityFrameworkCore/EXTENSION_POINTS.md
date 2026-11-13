# Dashboard definition persistence

`StudioDashboardEntityFrameworkCore` depends on native Dashboard Management. It supplies one scoped `IDashboardResourceStore` through `EfDashboardResourceStore<TContext>`; duplicate stores are rejected by public registration. See the [Core contracts](../../Core/EXTENSION_POINTS.md) and [management catalog](../../Management/EXTENSION_POINTS.md).

`AddDashboardResourcesEntityFrameworkCore<TContext>` supports an ordinary consumer DbContext; the consumer owns its model, provider and migrations and calls `AddDashboardResourcesModel`. Resource operations create independent contexts through the consumer's normal registration so they do not flush or clear unrelated tracked work.

The built-in feature composes SQLite, SQL Server, PostgreSQL and MySQL using their provider-specific contexts, migration history `ElsaStudioDashboard`, and schema family `StudioDashboard`1.0.0. The host supplies its selected provider package. Store ReadPageAsync is bounded to16 current documents, with history omitted; point reads carry history for atomic mutation/history operations. The package introduces no events, contributors or background tasks. See [persistence behavior and limits](README.md).
