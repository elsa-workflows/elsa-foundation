# Company.Connector

An Elsa 4 connector package. CLR reconciliation discovers `IActivity` types from `[Activity]`, `[ActivityInput]`, `[Version]`, and `[TriggerActivity]`. Do not add a second activity catalog.

Scaffolded with `dotnet new elsa-connector`.

- Field tiers: `[ActivityInput(Tier = InputTier.Essential|Optional|Setting)]`
- Icons: `[Activity(Icon = "plug")]`
- Secrets: `[ActivityInput(IsCredential = true)]` and `ISecretValueResolver` via `IConnectionContext`
- HTTP: the named `Elsa.Activities.Http` client, with connection auth as a delegating handler
- Activities only: no `IShellFeature`, hosted services, or EF contexts
