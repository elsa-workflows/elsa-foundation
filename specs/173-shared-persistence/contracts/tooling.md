# Resource-aware persistence tooling contract

Status: Integrated design review approved on 2026-09-23 for #1967 and published in PR #1973. The shared-resource implementation is in draft PR #1974 under #1968, with focused protocol tests and a disposable PostgreSQL Workbench receipt. Final current-head acceptance remains T045; the separate diagnostics layout belongs to #1969.

This contract specializes the reviewed [configuration-context decision](../decisions/tooling-configuration-context.md), [target-selection decision](../decisions/tooling-target-selection.md), and [strict verification decision](../decisions/tooling-target-verification.md). Configuration shape and enrollment belong to [persistence-configuration.md](persistence-configuration.md). No generic secret-provider or database-identity service is introduced.

## Public command selection

Add `--configuration-context <source>` and `--resource <name>` to persistence list, plan, script, apply, validate and post-migrate where the underlying command accepts host selection. Script-check takes its selection exclusively from the committed manifest, as required by the existing contract; it does not accept these new selection flags. The accepted sources are exactly `workbench-json-v1` and `workbench-json-environment-v1`. Explicit context requires exactly one existing `--shell`; environment uses the existing `--environment` selector, default `Production`. A resource requires explicit context. Existing provider, schema, module-selection and command requirements still apply.

The first source reads appsettings.json, appsettings.<environment>.json, shells.json, shells.<environment>.json in that order from the selected validated host directory. The second adds the standard .NET environment provider using the worker's inherited environment. No implicit ASPNETCORE_ENVIRONMENT selection, runtime command-line source, user secrets, vault or custom provider is captured. Output describes agreement with this supplied context, never observation of a separate running host.

Branch before the front end's legacy feature/provider projection. The front end transports metadata, not persistence interpretations or configuration values. Worker fields for validated host directory, host identity, shell and environment remain authoritative; the additions are context version/source and optional resource only. Do not introduce a second nested host selector. Bump the co-shipped, private CLI-to-worker closed envelope to version 2 and update its exact-field/security tests; do not silently add fields to its closed version 1. This private version is independent of host operation, context and manifest versions. The same CLI/worker distribution must agree; an incompatible private worker refuses before host invocation.

## Host enrollment and snapshot

The selected host assembly declares exactly one `EfToolingShellDefaultsAttribute(Type composerType)`. Its concrete public parameterless composer implements:

```csharp
public interface IEfToolingShellDefaults
{
    void Configure(ShellBuilder builder, IConfiguration configuration);
}
```

The attribute and interface belong to `Elsa.Persistence.EntityFramework.Tooling`. Runtime `AddEfPersistenceResources` requires that same declaration on the explicitly supplied host assembly. Workbench shares one composer between ConfigureAllShells and tooling. Absence preserves legacy-only hosts; duplicate declarations, invalid types and mismatched host assembly/layout refuse resource mode.

The composer only selects/configures a ShellBuilder. It may attach deferred feature actions but must not invoke them, construct features, create shell services or access a database. Tooling composes a fresh builder through that composer, public `ShellBuilder.FromConfiguration`, `FeatureDiscovery.DiscoverFeatures` and `FeatureDependencyResolver.GetOrderedFeatures`. It preserves reset/disable and configurator identity. The final dependency graph controls applicability, including disabled features reintroduced as required dependencies. Resource-applicable opaque configurators and disabled-required conflicts refuse before feature effects.

The configuration adapter lives beside EfToolingHost. It owns explicit JSON/environment/CShells package references; the CLI and worker remain EF-free. Source reads produce one frozen per-invocation snapshot with reload disabled. Raw configuration and presence data never cross the host boundary.

## Reflected entry points and lifetime

Retain legacy `RunAsync(Stream request, Stream response)`. Add exactly:

```csharp
public static EfToolingConfigurationContext CreateConfigurationContext(
    Stream request, CancellationToken cancellationToken);

public static Task<int> RunAsync(
    Stream request, Stream response,
    EfToolingConfigurationContext context, CancellationToken cancellationToken);
```

`EfToolingConfigurationContext` is sealed, host-owned and IDisposable, with an internal constructor and no public configuration/secret projection. The worker negotiates the exact factory, overload, context type and supported version together before invocation. Partial presence refuses; never fall back after a capability, parsing or execution failure.

The factory accepts a closed camelCase JSON object:

| Field | Contract |
|---|---|
| `contextVersion` | Integer 1; unknown versions refuse. Independent of operation protocol version. |
| `source` | One of the two source identifiers above. No-selector probe uses files mode explicitly. |
| `hostDirectory` | Existing worker-validated canonical directory, internal only. |
| `hostName` | Existing selected host identity, checked against the loaded host assembly/layout. |
| `environment` | Resolved existing selector, nonblank. |
| `shell` | Exactly one explicit shell; null only for a no-selector probe of the existing whole-host selection scope. |
| `explicitSelection` | True for the new command flag; false only for internal compatibility inspection. |

The worker holds the returned object opaquely and passes the identical instance to sequential internal listing and final script/other operation calls. It never serializes or casts the snapshot to IConfiguration. Resource/module selection and actual connection belong to each operation request, not the context. Dispose in try/finally on success, refusal and cancellation; disposal is idempotent and use after disposal refuses. Factory, synchronous reflection, awaited invocation and disposal exceptions all cross the same redacted boundary. Cancellation reaches both new methods directly.

## Closed operation protocol version 2

Use separate closed version-2 request/response DTOs at the context overload. Do not add optional fields to the closed legacy version-1 DTOs or change their serialization. Case-sensitive camelCase fields and rejection of unknown fields/commands/enum values remain the contract.

Version 2 retains version-1 command fields `command`, `selection`, `provider`, `schema`, `output`, `engine`, `packages`, and `connection` with their existing command-specific restrictions. `version` must be 2; add optional `resource`. Omit and reject `host`, `shells` and `capabilitySelection`: the host derives those facts from its context. Host identity and provider-agreement evidence have one authority, the validated context; no duplicated caller assertions are accepted. Existing engine/package facts retain their validated closure meaning.

Add internal `inspect-context` only for `explicitSelection=false`. It accepts version, command and existing module selection; it rejects resource, connection, output, script facts and all database/artifact operations. An explicit context rejects this internal command. Unknown versions never retry as version 1. Sending `resource` to the legacy entry point is a closed-contract refusal.

Version-2 responses retain the version-1 status/exitCode/command and exactly-one-payload discipline, with `version: 2`. Add an `inspectContext` payload for inspect-context with exactly one field, `outcome` (`no-resource-applicable` or `legacy-only`). configurationContext.resolution must equal that outcome; unknowns remain in configurationContext.unresolved. The worker branches only on this typed success payload, never on optional evidence or a legacyAllowed boolean. Positive applicability returns the ordinary error payload with configuration-context-required. The worker must deserialize and validate the closed response, version, requested command, status/exitCode and exactly-one matching payload before reporting or forwarding it; arbitrary JsonDocument parsing is insufficient. Keep legacy response handling unchanged. They additionally carry `configurationContext` when a context was successfully established. Its closed redacted fields are:

| Field | Allowed content |
|---|---|
| `source` | Selected source identifier. |
| `environment`, `shell` | Canonical selector identities; shell null only for a whole-host probe. |
| `resource` | Requested resource identity or null. |
| `resolution` | `resource`, `legacy`, `no-resource-applicable`, or `legacy-only`. |
| `targetVerification` | `not-performed`, `matched`, or `refused`. |
| `runtimeParity` | Always `unobserved`; this tool does not inspect a running host. |
| `participants` | Stable feature/module/resource/provider/connection-reference identities and selection provenance, sorted deterministically. No feature option bags. |
| `unresolved` | Stable codes with relevant non-secret identities, sorted deterministically. |

Reuse the model's detached evidence representation internally. Only the host constructs these facts; the worker preserves them without inferring success. Offline output uses `not-performed` and lists applicable live prerequisites. Matching a supplied connection does not claim that later migration, activation or publication succeeded. Early errors may omit context facts; typed error code and safe message still identify the refusal.

## No-selector compatibility

Every invocation against a host with the complete new API performs inspect-context before choosing the original legacy path. A declared composer is always evaluated, including code-only defaults. Applicability means an enabled enrolled owner of a selected module has an effective resource selection, including malformed or missing selected references.

| Host/probe result | Required behavior |
|---|---|
| Composer; applicable resource intent | `configuration-context-required`, before database or artifact work. |
| Composer; no applicable intent | Typed `no-resource-applicable`; unknowns remain unresolved. Allow original legacy request. |
| No composer; new resource-key hint | `host-composition-unavailable`; do not assert semantic applicability. |
| No composer; no hint | Typed `legacy-only` with `host-not-enrolled` explanation. Allow original legacy request without claiming a graph check. |
| Explicit context; no composer | `host-not-enrolled` refusal. |
| Older API with new resource-key hint | `context-capability-unavailable`; no downgrade. |
| Older API without hint | Preserve legacy path and its existing limitations. |

Hints cover only Resources, root/shell DefaultResource and shell Bindings, including null presence. They do not cover the whole Elsa:Persistence namespace or ordinary legacy EF policy settings. Definitions alone are inert when a capable composer can evaluate them. Environment-only intent stays outside no-context execution, so neither legacy branch proves external resource absence or live parity. Only the two typed negative new-API outcomes permit legacy execution; malformed or unresolved applicability never does.

## Resource selection and live ordering

The [target-selection decision](../decisions/tooling-target-selection.md) is normative for aliases, module ownership and context constraints. A declared target group is canonical provider plus connection-reference identity in the same configuration snapshot. It is not physical database identity.

- `--from-host` selects all enabled enrolled module candidates in the requested group, including dependencies. `list` with resource and no module selector uses that group.
- Explicit `--modules` preserves the exact set; each module must belong to the group. `--all` preserves all discovered modules and refuses if any is outside the group. Never silently filter either selector.
- Validate every enabled owner of every selected module/context, including owners using another reference or legacy options. Known context-option conflicts refuse. Unresolved mixed-owner options prevent live execution.
- Keep existing dependency ordering/missing-dependency checks. Do not add a dependency from another target group automatically. A group-scope refusal is not a claim of physical incompatibility.
- Context-only list/plan may describe the shell. Script and live operations involving resource participants require explicit resource selection; never infer it from one catalog entry or the supplied actual connection. Wholly legacy module selections retain legacy semantics and report no resource verification.

Offline list/plan/script/script-check validate selected resource shape, provider/reference identity, module scope and known constraints. They never look up expected named connection values, including checks for their presence. Distinct references required to share one context or local transaction yield `target-affinity-unverified` in `configurationContext.unresolved`; this is not a claim that their physical targets differ. Missing actual secret values are live prerequisites, not offline plan failures. Existing offline migration/model construction may still occur after configuration checks; the no-construction guarantee below applies to live target refusals.

For apply/validate/post-migrate, complete identity, scope and option checks first. Obtain actual connection only from existing explicit env/stdin input. Resolve every distinct expected connection reference among all selected context owners inside the snapshot, memoized within the operation. Compare each with the supplied actual connection using existing strict equality. Missing/empty expectation or mismatch refuses before DbContext construction, connection creation or database commands. Neither input supplies a missing value for the other. Only then run existing host-owned module operations. Preserve operation-specific transaction constraints and publication's ordered multi-context behavior.

## Manifest and script-check

Legacy script output keeps migration-plan schemaVersion 1 byte-for-byte behavior. Explicit-context script emits schemaVersion 2: all existing manifest fields and module ordering remain, with one `configurationContext` object after `host` containing the redacted response facts above. Its offline targetVerification is always `not-performed`; runtimeParity is `unobserved`. Include no timestamp, raw path, process state, secret value or connection hash. The retained host.providerAgreement is `checked` only when the existing authoritative provider agreement succeeds against the context; otherwise `not-checked`. It does not mean a live connection match. Both manifest and Report render the actual context source explanation, never the old unconditional file-only note. SQL normalization and artifact hashes remain unchanged.

MigrationPlan.Read discriminates schemaVersion 1 versus 2 and validates each closed shape. For version 2, script-check reconstructs source/resource/shell/environment and module/provider selectors from the committed manifest, then regenerates through the matching context protocol. It takes only the selected host path from --host and its canonical identity from HostLayout. A committed host-name mismatch refuses or appears as an explicit comparison difference; never overwrite canonical factory identity with the artifact name. The manifest version chooses the regeneration mode; invalid/unknown versions refuse. Context/evidence differences with identical SQL report “manifest differs, SQL identical”, not the existing misleading “manifest versions differ” classification. Preserve version-1 behavior and existing stale-file/deterministic-byte checks. Context creation is once per worker invocation, including script's internal package listing; changes to files between subcalls cannot change the snapshot's selection.

## Refusals and evidence

Use existing exit-code categories: usage for invalid selector/version/closed shape, resolution for invalid resource/context/target/capability, and existing operation categories after checks. New stable resolution codes include configuration-context-required, context-capability-unavailable, host-not-enrolled, host-composition-unavailable, configuration-context-invalid, configuration-context-disposed, resource-required, resource-target-scope, expected-connection-unresolved and connection-target-mismatch. Configuration/source/compose/reflection/disposal failures use configuration-context-invalid with a safe phase identifier, never an underlying message. Existing resolver codes remain those in the persistence contract.

Never emit either connection value, hashes of those values, raw configuration/paths or provider/JSON/reflection exception details. Cover stdout, stderr, thrown exceptions, serialized responses, logs, manifests and process arguments using canaries. Public resource/reference/feature identities must be treated as data and rendered safely. Cancellation retains the existing cancellation outcome and cleanup behavior without a successful operation claim.

Required proof is in [quickstart.md](../quickstart.md): both source modes, old/partial/unknown API compatibility, all public command paths, same-context intermediate calls, zero expected lookups offline, zero context/connection/database creation on live mismatch, alias/shared-context checks, secret canaries, deterministic versioned artifacts, and real rebuilt-host/database agreement. Unit success alone cannot report the layout ready.

## Implemented evidence and limits (2026-09-24)

| Contract area | Executable evidence | Limit |
|---|---|---|
| `workbench-json-v1` and `workbench-json-environment-v1` | `EfToolingConfigurationContextTests` and public `ConfigurationContextCliTests` cover source ordering, frozen context, explicit shell/environment, and absence of implicit external sources. | A separate running host's environment is never observed; `runtimeParity` remains `unobserved`. |
| Context evidence and compatibility | `EfToolingHostTests`, `WorkerProtocolTests`, `WorkerRunnerConfigurationContextTests`, `ToolingEntryPointTests`, and `ScriptCheckCliTests` cover the closed v2 envelope, redacted participants/unresolved codes, v1 compatibility, partial/unknown-host refusal, cancellation and disposal. | Offline `targetVerification: not-performed` does not imply a live connection match. |
| Module-set selection | EF host tests cover dependency-enabled owners, `--from-host`, explicit modules, and group-scope refusal. The Workbench PostgreSQL receipt selects the four default-shell shared modules and verifies their migration histories. | This does not establish a separate diagnostics target or equivalence of every opt-in Runtime store. |
| Live expected-target and side effects | `ResourceAwareLiveCliTests` and the public CLI tests cover `apply`, `validate`, and `post-migrate`: a mismatch refuses before DbContext/post-action construction and SQLite file creation. The PostgreSQL receipt checks same-target CLI validation and mismatched-target refusal. | Direct ADO connection-object construction is not separately instrumented; final current-head gate is T045. |
| Secrets and artifacts | `PersistenceCliTests`, `WorkerLaunchTests`, and `ScriptCheckCliTests` exercise synthetic canaries across output, errors, process arguments, v2 manifests and regeneration differences with identical SQL. | Canary absence is scoped to the tested paths and fixtures. |

The exact commands and observed counts are in the [T019/T023/T038 checkpoint](../quickstart.md#t019t023t038-tooling-contract-checkpoint-2026-09-24) and [shared-layout receipt](../quickstart.md#t015-live-shared-layout-checkpoint-2026-09-24). These are implementation evidence; this contract remains the reviewed selection and refusal rule.
