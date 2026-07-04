# W21 / MD-10 — Feature-registration-test (§2.23.1) gap list

Status: report (actionable gap list). Produced by W21 of the Elsa 4 remediation fleet.

**Branch point:** `1d5bb6bb` (W18 merge tip). **Build baseline:** 0 errors.
**Snapshot caveat:** the feature set below is a snapshot at this SHA. Parallel wave units
W16 (adding feature projects) and W17 (extracting `Publishing.Core`) will shift the counts;
this report does not chase their branches. Re-run the method (below) after they merge.

## What §2.23.1 requires

> Every feature class MUST have a unit test that constructs the feature, invokes its registration
> entry point against an `IServiceCollection`, builds the `IServiceProvider`, and asserts that every
> service the feature is expected to register **resolves.** — framework constitution §2.23.1.

The audit answers one question per feature class: *is there a test that exercises this feature's own
registration?* — measured by whether the feature class is directly constructed (`new X()`) or
type-referenced (`typeof(X)`) anywhere under `tests/`.

## Method (reproducible)

1. Enumerate concrete feature classes from source: a `public [sealed] class *Feature` that either
   implements `IShellFeature` / a `*FeatureBase`, or carries the `[ShellFeature(...)]` attribute.
   Abstract base classes are excluded (they carry no direct §2.23.1 obligation; their concrete
   subclasses do).
2. Cross-reference each against `tests/**/*.cs` for `new <Feature>(` … `)` (word-boundary, so
   object-initializer `new X { … }` also matches) **or** `typeof(<Feature>)`.
3. A feature is **covered** if either idiom appears; otherwise it is a **gap**.

This is the same construction-based measure the 2026-07 review used (`review-modularity.md`, MD-10),
so the numbers are comparable. It is a *lower bound* on true §2.23.1 intent: a feature whose
`ConfigureServices` merely delegates to a tested extension method (e.g. `SecretsFeature` →
`AddSecrets()`) is wired-in-spirit but still counts as a gap here, because the constitution asks for
the **feature class itself** to be constructed.

## Headline tally at `1d5bb6bb`

| Measure | Count |
|---|---:|
| Concrete feature classes | **70** |
| Covered (before W21) | 47 |
| **Closed in W21** (pattern-stamped registration tests) | **+5** |
| **Covered after W21** | **52** |
| **Remaining gap** | **18** |

The 2026-07 review reported ~38/65; the increase to 47/70 is partly real new coverage and partly a
methodology fix — the review's grep required `new X(` immediately followed by `(`, which missed the
several persistence-shell features constructed via object-initializer syntax (`new X { … }`).

## Closed in W21 (5 — pattern-stamped, tests/ only, no new infrastructure)

Each new test mirrors the existing `tests/Elsa/Architecture/FeatureRegistrationTests.cs` pattern
exactly (construct the feature, call `ConfigureServices`, assert the owned contract resolves or is
registered). Home test projects that already referenced the feature project took the test with **zero
csproj change**; the two Expressions features required only added `ProjectReference` entries (no new
fixtures/base classes/hosts).

| Feature | Source | Test added | Assertion |
|---|---|---|---|
| `MediatorFeature` | `Elsa/Mediator/MediatorFeature.cs:18` | `tests/Elsa/Mediator/Tests/MediatorFeatureRegistrationTests.cs` | resolves `IRequestSender`, `ICommandSender` |
| `MemoryCacheFeature` | `Elsa/Caching/Memory/MemoryCacheFeature.cs:21` | `tests/Elsa/Caching/Tests/MemoryCacheFeatureRegistrationTests.cs` | `ICacheManager` registered |
| `SecretsFeature` | `Elsa/Secrets/Features/SecretsFeature.cs:16` | `tests/Elsa/Secrets/Tests/SecretsFeatureRegistrationTests.cs` (added fact) | resolves `ISecretManager` |
| `LiquidExpressionsFeature` | `Elsa/Expressions/Liquid/LiquidExpressionsFeature.cs:29` | `tests/Elsa/Expressions/Tests/ExpressionsFeatureRegistrationTests.cs` | `ILiquidTemplateManager` registered |
| `JavaScriptLibrariesFeature` | `Elsa/Expressions/JavaScript/Libraries/JavaScriptLibrariesFeature.cs:19` | `tests/Elsa/Expressions/Tests/ExpressionsFeatureRegistrationTests.cs` | `IScriptPreProcessor` registered |

## Remaining gap (18) — grouped by why each was NOT trivially closeable in W21

The rule for W21's trivial cut was: *closeable with a pattern-stamped test, in an existing test
project that already references (or can reference with one `ProjectReference`) the feature, with no
new test infrastructure.* The 18 below each fail that rule for the stated reason.

### A. Simple feature, but no home test project exists (3)
Closeable in one line, but would require **creating a new test `.csproj`** (new infrastructure) —
out of W21's zero-infra scope. Recommend a small follow-up that adds a `Tests` project per domain.

| Feature | Source | Note |
|---|---|---|
| `FileSystemLockingFeature` | `Elsa/Locking/FileSystem/FileSystemLockingFeature.cs:18` | no `tests/Elsa/Locking` project; registers `IDistributedLockProvider` |
| `HttpFeature` | `Elsa/Http/HttpFeature.cs:21` | no `tests/Elsa/Http` project; registers `IRouteTable` + parsers |
| `HttpJavaScriptFeature` | `Elsa/Http/JavaScript/HttpJavaScriptFeature.cs:19` | no `tests/Elsa/Http` project; registers `IJavaScriptDeclarationContributor` |

### B. JavaScript feature needing engine/expression wiring (3)
Registration resolves through the Jint/JS engine graph; a faithful test wants the JS test host rather
than a bare `ServiceCollection`. Descriptor-assertion stamps are possible but the descriptor set is
non-obvious; recommend co-locating with the existing JS test hosts.

| Feature | Source |
|---|---|
| `JavaScriptActivitiesFeature` | `Elsa/Workflows/Runtime/JavaScript/JavaScriptActivitiesFeature.cs:18` |
| `JavaScriptRenderingFeature` | `Elsa/Expressions/JavaScript/Rendering/JavaScriptRenderingFeature.cs:24` |
| `JavaScriptWorkflowsDesignFeature` | `Elsa/Workflows/Design/JavaScript/JavaScriptWorkflowsDesignFeature.cs:18` |

### C. Persistence-shell feature needing DB/Groundwork scaffolding (5)
These configure an EF Core / Groundwork document store; a registration test needs the persistence test
host (as the *non-unified* siblings already have — e.g. `GroundworkRuntimePersistenceRegistrationTests`,
`GroundworkWorkflowsDesignRegistrationTests`). The pattern exists; it is just not one-line.

| Feature | Source |
|---|---|
| `IdentityGroundworkPersistenceFeature` | `Elsa/Foundation/Identity/Persistence/Groundwork/IdentityGroundworkPersistenceFeature.cs:16` |
| `SecretsGroundworkPersistenceFeature` | `Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkPersistenceFeature.cs:16` |
| `SqliteWorkflowsDesignPersistenceShellFeature` | `Elsa/Workflows/Design/Persistence/EFCore/Sqlite/SqliteWorkflowsDesignPersistenceShellFeature.cs:24` |
| `SqliteGroundworkUnifiedPersistenceShellFeature` | `Elsa/Persistence/Groundwork/Sqlite/Unified/SqliteGroundworkUnifiedPersistenceShellFeature.cs:24` |
| `PostgreSqlGroundworkUnifiedPersistenceShellFeature` | `Elsa/Persistence/Groundwork/PostgreSql/Unified/PostgreSqlGroundworkUnifiedPersistenceShellFeature.cs:24` |

### D. API / endpoint feature needing the FastEndpoints host (5)
These layer FastEndpoints request handlers; a registration test needs the API test host
(`TestFastEndpointsFeature` / `ApiSecurityRegistrationTests` show the shape).

| Feature | Source |
|---|---|
| `SecretsApiFeature` | `Elsa/Secrets/Api/Features/SecretsApiFeature.cs:18` |
| `WorkflowsDesignApiFeature` | `Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs:19` |
| `WorkflowsRuntimeHttpFeature` | `Elsa/Workflows/Runtime/Http/WorkflowsRuntimeHttpFeature.cs:20` |
| `JavaScriptActivitiesEndpointsFeature` | `Elsa/Workflows/Runtime/JavaScript/JavaScriptActivitiesEndpointsFeature.cs:16` |
| `JavaScriptRenderingEndpointsFeature` | `Elsa/Expressions/JavaScript/Rendering/JavaScriptRenderingEndpointsFeature.cs:16` |

### E. Miscellaneous (2)
| Feature | Source | Note |
|---|---|---|
| `ActivitiesCompositionRuntimeFeature` | `Elsa/Activities/Composition/Runtime/ActivitiesCompositionRuntimeFeature.cs:24` | composition-runtime wiring; evaluate alongside the Composition design feature tests |
| `Elsa3ImportActivitiesFeature` | `Elsa3/Activities/Design/Import/Elsa3ImportActivitiesFeature.cs:19` | elsa3 import boundary; `Elsa3MappingFeature` is covered, this sibling is not |

## Recommended follow-ups (not executed in W21)

1. **Domain test-project follow-up (Group A):** add `tests/Elsa/Locking` and `tests/Elsa/Http`
   projects, then stamp the 3 one-line registration tests. Small, low-risk.
2. **Persistence-shell follow-up (Group C):** stamp registration tests against the existing Groundwork
   persistence test host, mirroring the non-unified siblings.
3. **API host follow-up (Group D):** stamp against `TestFastEndpointsFeature`.
4. Consider a **generated guard** that fails when a `[ShellFeature]`-attributed concrete class has no
   corresponding registration test, so this audit becomes mechanical rather than periodic (the review's
   MD-9 shows the repo already sustains this discipline for `EXTENSION_POINTS.md`).

## Links

- Finding source: [`review-modularity.md` §MD-10](elsa-4-architecture-review-2026-07/review-modularity.md)
- Roadmap brief: [W21](elsa-4-architecture-review-2026-07/roadmap.md)
- Bucket: [Elsa 4 review remediation](../program-goals/elsa-4-review-remediation.md)
- Gate: framework constitution §2.23.1
