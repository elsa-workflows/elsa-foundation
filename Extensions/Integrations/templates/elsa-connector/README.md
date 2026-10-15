# Company.Connector

An Elsa 4 connector package. CLR reconciliation discovers `IActivity` types from `[Activity]`, `[ActivityInput]`, `[Version]`, and `[TriggerActivity]`. Do not add a second activity catalog.

Scaffolded with `dotnet new elsa-connector`.

- Field tiers: `[ActivityInput(Tier = InputTier.Essential|Optional|Setting)]`
- Icons: `[Activity(Icon = "plug")]`
- Secrets: `[ActivityInput(IsCredential = true)]`, resolved through foundation `ISecretValueResolver` at activation
- HTTP: the named `Elsa.Activities.Http` client, with optional `PushHttpAuth` for the duration of a request
- Activities only: no `IShellFeature`, hosted services, or EF contexts
- Service connections (`[ActivityConnection]`, the `http` type) land in `Elsa.Connections` (#2729), not this package
