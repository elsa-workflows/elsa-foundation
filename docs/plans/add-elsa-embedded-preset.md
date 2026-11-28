# AddElsa() embedded preset

**Status:** proposed ([#4147](https://github.com/elsa-workflows/elsa-foundation/issues/4147))
**Date:** 2026-11-11
**Place:** `docs/plans/` — this repository's shelf for design notes and plans of record that precede
specs and ADRs (same as `studio-bearer-token-issuance.md` and `connections-and-secrets-model.md`).
Not an ADR (the issue asked for a design note), not a report, not the shipped CLI recipe
([embedded-runtime-profile.md](../reference/embedded-runtime-profile.md)).
**Follows:** program [#4132](https://github.com/elsa-workflows/elsa-foundation/issues/4132),
[#2570](https://github.com/elsa-workflows/elsa-foundation/issues/2570), assessment
[#4124](https://github.com/elsa-workflows/elsa-foundation/issues/4124) §1, §3 (E-1, E-7), Q4, Q5;
[ADR 0080 D3](../adr/0080-elsa-4-simplification-decisions.md#d3--internal-by-default);
ADR 0097 (g) ([draft PR #4156](https://github.com/elsa-workflows/elsa-foundation/pull/4156)).
**Next:** [#4148](https://github.com/elsa-workflows/elsa-foundation/issues/4148).

## Call shape

Fluent `AddElsa` plus a preset method, **not** a closed preset enum. Matches #2570, Elsa 3
`AddElsa(...)`, and Studio's `AddElsaStudio`. Later presets add methods; they do not widen an enum.
Namespace `Elsa.Hosting`. `ElsaBuilder`'s constructor is `internal`. Signing keys live in
configuration, not in `Program.cs`.

```csharp
using Elsa.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddElsa(elsa => elsa
    .UseEmbedded()
    .UseSqlite("Data Source=elsa.db"));
var app = builder.Build();
app.MapGet("/", () => "host app");
app.MapElsa();
app.Run();
```

`UseEmbedded()` is required for this slice. `UseSqlite` / `UsePostgreSql` / `UseSqlServer` bind the
named persistence resource. In a web host `MapElsa()` is required: it maps shell routes, then
`UseAuthentication` / `UseAuthorization` (Workbench order: auth after `MapShells()`).

## What `UseEmbedded` registers

Internally it **wraps** CShells (Q4):

1. `AddCShellsAspNetCore` with host assemblies, authentication/authorization, configuration, and path routing.
2. An in-memory blueprint for shell `default` (overridable): the 16 `embedded-runtime@1` feature IDs
   **plus** `FoundationIdentityAbstractions` (needed for a clean 401). No IAM, OpenIddict, or ASP.NET
   Identity stores — [ADR 0097 (g)](https://github.com/elsa-workflows/elsa-foundation/pull/4156).
3. A shell-activation hosted service (`GetOrActivateAsync(shellName)`) with readiness and error
   logging. The migrations off-switch is E-8, not this slice.
4. An **internal** empty `IEfToolingShellDefaults` composer on Elsa's hosting assembly.
   `AddEfPersistenceResources` uses that assembly so the host writes no `[assembly: EfToolingShellDefaults]`.
5. Persistence resource binding (`Elsa:Persistence` plus the `Use*` connection string). The EF engine
   package remains a host reference (packaging is owner-gated).
6. `AddPersistenceCore(TenantId)` with `TenantId` default `PersistenceScope.DefaultValue` (`"default"`).
   No tenant catalog, no HTTP resolver. Uses the existing `SinglePersistenceScopeSource`; #4139 may
   replace the source later without changing this call shape.
7. Signing keys from configuration. Missing keys fail closed at activation.
8. A fallback challenge scheme only when the host registered none, so anonymous `GET /elsa/capabilities`
   is 401, not 500. An existing host default scheme is left alone. Host principals stay un-normalized
   until #4153.

Workbench and Foundation.Host stay unchanged. `UseEmbedded` refuses if CShells is already registered
(same-process Studio is Phase 2).

## Middleware and the #4152 hook

Order today: `MapShells()`, then `UseAuthentication()`, then `UseAuthorization()`. `MapElsa()`
performs that sequence so the 10-line host does not learn it.

Reserved for #4152, **not** implemented in #4148: `ElsaBuilder` stays stable for a future
`UseHostAuthentication(...)` extension; `MapElsa` does not map a host principal onto Elsa
permissions; the bound persistence scope for that later bridge is the same single `TenantId`
(ADR 0097 (g)).

## Q4 — wrap versus hide CShells (**pending owner**)

**Recommended default: wrap, do not hide.** `AddElsa` / `UseEmbedded` / `MapElsa` are the documented
path; they never require `shells.json`, feature IDs, or a tooling-defaults class. CShells remains the
composition engine.

- **Docs.** The 10-line path never mentions shells. Workbench, Foundation.Host, and
  `dotnet elsa composition` keep teaching shells where they already do.
- **`shells.json`.** Not required. An advanced host may still add one; the preset merges, and
  `Elsa:Embedded:Path` wins for the default shell's web path unless the host already set it.
- **Studio hosting.** Wrapping leaves E-6 able to reuse one `AddCShellsAspNetCore`. Hiding CShells
  completely would force a second composition stack and block E-12 (thin Workbench on the same builder).

## Q5 — one AddElsa versus a parallel API (**pending owner**)

**Recommended default: this is a #2570 slice under program #4132.** There is one `AddElsa` surface.
#4148 delivers the Embedded preset; later #2570 slices add `AddActivitiesFrom` / `AddWorkflowsFrom` /
`UsePersistenceMode` / the console hello-world on the same `ElsaBuilder`. Do not invent a second
entry point. #4132 already folds E-1 into #2570; this note does not reopen that as a competing API.

## Host-service sharing (E-7, later)

Not in #4148. Today: `ShareWithShells<T>()` in `Elsa.Cluster.Readability`. A later
`ShareHostService<T>()` on `ElsaBuilder` is a convenience alias. Document in embedding docs page 4.

## Composition

| Surface | Relation |
|---|---|
| `embedded-runtime@1` | Feature ID set `UseEmbedded` enables (plus Identity abstractions). The profile stays the CLI/catalog pin; `AddElsa` does not call the CLI. |
| `dotnet elsa composition` | Unchanged for modular hosts. Embedded hosts skip it. |
| Elsa 3 `AddElsa` | Same fluent mental model; Elsa 4 still isolates in a shell. The migration guide is E-9. |
| Other #2570 presets | Same `AddElsa` + `ElsaBuilder`; new `UseX` methods. `UseEmbedded` is the first. |
| Studio | Out of this slice. Do not call `AddElsaStudio` in the same process until E-6. |

## Non-goals

Package ids, scenario meta-packages, nuget.org, BOM (Q3, owner-gated). `UseInMemory`. Authoring
features (#4150, #4151). Host-auth (#4152 / #4153). Migrations switch (E-8). Studio in-process
(E-6). Changing Workbench or Foundation.Host.

## Public types #4148 must create

ADR 0080 D3: host-author entry points and options only. Activation, tooling defaults, the fallback
scheme, and the in-memory blueprint are `internal sealed`. Place the types in an existing hosting
project that can reference `CShells.AspNetCore` and the embedded-runtime features, or a new project
if none fits. The package id is not decided here.

| Type | Members #4148 adds |
|---|---|
| `ElsaServiceCollectionExtensions` | `AddElsa(this IServiceCollection, Action<ElsaBuilder>? configure = null)` |
| `ElsaBuilder` | `IServiceCollection Services { get; }`, `IConfiguration Configuration { get; }` |
| `ElsaEmbeddedOptions` | `Path` (default `elsa`), `ShellName` (default `default`), `TenantId` (default `default`), `PersistenceResource` (default `primary`), `ConnectionName` (default `Elsa`), `LocksFolderPath` (default `locks`) |
| `ElsaEmbeddedBuilderExtensions` | `UseEmbedded(this ElsaBuilder, Action<ElsaEmbeddedOptions>? configure = null)` |
| `ElsaPersistenceBuilderExtensions` | `UseSqlite(this ElsaBuilder, string connectionString)`, `UsePostgreSql(...)`, `UseSqlServer(...)` |
| `ElsaEndpointRouteBuilderExtensions` | `MapElsa(this IEndpointRouteBuilder)` |

Do **not** create in #4148: `ShareHostService`, `UseHostAuthentication`, `UsePersistenceMode`,
`AddActivitiesFrom`, `AddWorkflowsFrom`, `UseInMemory`, a public tooling-defaults type, a public
activation hosted service, a preset enum.

## Config keys #4148 must create

New documented host keys. The preset copies them into the in-memory shell; the host never writes
`shells.json` for this path.

| Key | Default | Role |
|---|---|---|
| `Elsa:Embedded:Path` | `elsa` | WebRouting prefix (`/elsa`) |
| `Elsa:Embedded:ShellName` | `default` | Shell to activate |
| `Elsa:Embedded:TenantId` | `default` | Single fixed persistence scope (ADR 0097 (g)) |
| `Elsa:Embedded:PersistenceResource` | `primary` | Named EF resource |
| `Elsa:Embedded:ConnectionName` | `Elsa` | `ConnectionStrings` key (`UseSqlite` may set the value in memory) |
| `Elsa:Embedded:LocksFolderPath` | `locks` | `FileSystemDistributedLocking` path |
| `Elsa:Workflows:Runtime:RecoveryContinuationSigningKey` | (required) | Copied onto `WorkflowsRuntimeEntityFrameworkCore` |
| `Elsa:Workflows:Runtime:HierarchyCursorSigningKey` | (required) | Copied onto `WorkflowsRuntimeEntityFrameworkCore` |

Existing keys the preset also honors if already set: `ConnectionStrings:{name}`,
`Elsa:Persistence:DefaultResource`, `Elsa:Persistence:Resources:{name}:Provider`,
`Elsa:Persistence:Resources:{name}:ConnectionName`. `UseSqlite` / `UsePostgreSql` / `UseSqlServer`
write Provider and the connection string; they do not invent a second persistence model. Do not
document `CShells:Shells:...` as required for this path.
