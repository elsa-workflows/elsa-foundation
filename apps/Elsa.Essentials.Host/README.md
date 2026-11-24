# Elsa Essentials Host

The workflow-essentials flavor of the Elsa server. There are three hosts under `apps/`:

| Host | What it compiles in | For |
|---|---|---|
| `Elsa.Foundation.Host` | No feature. Every feature arrives as a package through the Nuplane feed. | A bare host you compose yourself. |
| `Elsa.Essentials.Host` | A curated set of workflow features, chosen by the maintainers. Add-ons still arrive through the feed. | Running workflows with Studio out of the box. |
| `Elsa.Workbench.Host` | Everything under development, including the extensions, plus test and QA instrumentation. | Developing and testing Elsa itself. |

## What is curated

The default shell in `shells.json` enables the essentials. The project file compiles in the assemblies those features live in:

- Activities: primitives, sequence, flowchart, control flow, HTTP, scheduling, dispatch workflow, and the graph runtime and design.
- Expressions: JavaScript (Jint) and Liquid.
- Workflows: design, validation, publishing, the distributed runtime on EF Core with resumption, scheduling, triggers, history, and the dashboard.
- Identity: ASP.NET Core Identity, OIDC and OpenIddict with Elsa's EF token store.
- Secrets, Studio preferences, Attention and module management.

The extensions (Agent, ETL, Rules, Decisions, Weaver, BPMN) and the diagnostics features are not curated yet; a deployment adds any of them as a feed package.

To change the set, add or remove the feature's `ProjectReference` in `Elsa.Essentials.Host.csproj` and its entry in `shells.json` (and in `shells.Production.json` when it has demo-only settings to blank).

## Running it

```bash
dotnet run --project apps/Elsa.Essentials.Host --launch-profile https
```

It listens on `https://localhost:7262` and `http://localhost:5262`. Development runs on SQLite (`elsa-essentials.db`), ephemeral identity keys, demo-only runtime keys and a seeded `admin` / `Password123!` user. Production (`ASPNETCORE_ENVIRONMENT=Production`) blanks those values. You must then supply the keys and the initial admin password as secrets, and apply migrations before the host starts (`Elsa:Persistence:EntityFramework:Migrate:Policy` is `Validate`).

`/health/live` and `/health/ready` are the probes. Point Studio at the host and add its origin to `Cors:AllowedOrigins`.
