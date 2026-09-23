# Runtime preparation and legacy management contract

Status: Integrated design review approved on 2026-09-23 for #1967. Published in PR #1973; implementation and runtime/database verification remain #1968/#1969 work.

The [persistence configuration contract](persistence-configuration.md) owns resource shape, presence, enrollment and resolution. The [tooling contract](tooling.md) owns the shared host-default declaration. This document specifies when runtime and management invoke that resolution and which side effects are permitted.

## Runtime enrollment

Add one root registration extension, `AddEfPersistenceResources(IServiceCollection services, IConfiguration configuration, Assembly hostAssembly)`, owned by Elsa.Modularity.EntityFramework. It validates that the explicit host assembly carries the tooling contract's single defaults-composer declaration. The declaration is mandatory for resource activation, including code-selected defaults. Do not scan unrelated loaded assemblies for a substitute. Legacy-only hosts need not register the adapter or declare a composer.

Registration installs exactly one CShells `IShellSettingsPreparer` and the EF management context-preparation implementation. Use the delivered CShells public preparation contract from preview.157, with the detailed catalog compatibility fix in preview.158. Duplicate preparers retain CShells' fail-fast refusal; do not create an implicit preparer chain. Root configuration is an explicit dependency because the CShells callback contains only composed shell configuration.

For each fresh shell generation:

1. CShells composes globals, shell selections and dependency expansion, then supplies its final settings-preparation context before feature construction, binding, configurator invocation or service registration.
2. The EF adapter collects root resource/default data, shell selection data and a narrow selected-shell authored legacy-target presence view. It uses the final ordered graph and metadata; it does not construct feature objects to discover defaults.
3. If configuration reload invalidates the input while collecting root and shell data, refuse/retry that candidate instead of combining generations. This is bounded change detection, not a transactional configuration-store guarantee.
4. Call the persistence-owned EfPersistencePreparation facade, which invokes the shared internal pure resolver. Validate resource applicability, explicit target ambiguity, configured provider/context constraints and opaque configurators before any participant effects.
5. Return only Provider and ConnectionName scalar patches for successfully resolved participants. Preserve untouched settings, unknown IDs/fields, typed-value identity and all legacy participants. Do not write Schema, Pooling, connection values or migration policy from a resource.
6. CShells applies patches to its fresh settings generation and continues its ordinary pipeline. Cancellation before/after preparation prevents activation as specified by the upstream hook.

Presence is determined from authored keys, not feature class initializers. Reset removes lower-priority raw feature target fields; a required dependency reintroduced despite disablement remains in the graph and refuses applicable resource activation. Resource definitions and bindings never enable features. Unknown consumers remain unenrolled/unresolved.

Runtime connection lookup remains the existing module/provider path after materialization. The preparation plan has no actual connection values. Shared-context and operation-specific transaction constraints remain owned by EF/domain integration; neither the new resource name nor a common provider proves transaction affinity.

## Reload and persistence

Explicit file/shell reload recomposes a fresh generation and reruns preparation. Generated Provider/ConnectionName values must never become authored defaults on the next generation. A failed candidate leaves the previously active generation available under the existing shell lifecycle. Verify this against a running rebuilt Workbench; a resolver test alone does not prove reload behavior.

Save only authored intent to the existing store. Do not save a generated plan, effective patches or secret-restored materialization as a new resource document. No new automatic data movement or migration-policy override occurs on a selection change. Successful activation does not itself prove data relocation.

The supported operator trigger is the existing Workbench root endpoint `POST /_admin/shells/reload/{name}` (URL-encode the selected shell name), mapped by Program.cs through CShells.Management.Api. It calls IShellRegistry.ReloadAsync directly. Authentication uses the existing `X-Elsa-Module-Management-Key` header and `Elsa:ModuleManagement:ApiKey` configuration; keep the credential server-side and out of shell commands/evidence. No configured key returns 404; an invalid/missing supplied key returns 401. This is host-control access under ADR 0037, not a new browser/user permission or resource editing API.

A completed handler returns the existing reload response with name, success, newShell, drain and error. HTTP 200 alone does not prove activation: assert success=true, the expected new generation and readiness. On candidate failure, assert success=false plus a safe error and verify the previous active generation remains usable. Wait for the host's configuration change notification in the fixture before invoking reload; changing a process's external environment requires restart rather than claiming JSON reload rereads process startup inputs. The existing shell-scoped ModularityApi registration already replaces its NullShellReloader with ShellReloader; do not rewire that unrelated fallback merely to expose the root reload operation.

Configuration-provider change detection is scoped to candidate construction. The existing Features-only management revision and non-atomic store writes do not establish concurrency protection for resources or root sources. Full preview/apply consistency, resource editing and saved-versus-activated recovery remain #1964 outcomes.

## Mandatory management preparation seam

Introduce one provider-neutral replacement service in Elsa.Modularity.Core using the existing context and refusal exception:

```csharp
public interface IFeatureActivationContextPreparer
{
    Task<FeatureActivationContext> PrepareAsync(
        FeatureActivationContext context,
        CancellationToken cancellationToken = default);
}
```

Return the prepared context on success; refuse by throwing the existing FeatureActivationRefusedException carrying existing redacted FeatureActivationRefusal entries. No new parallel result hierarchy is needed. The default returns the input context unchanged. FeatureManagementService constructs the context after RestoreSecrets and ValidateRequest, invokes preparation, then passes the returned context into EnsureActivationAllowedAsync instead of reconstructing it there. Save retains the original restored authored request. The existing API maps the refusal to HTTP 409.

The current snapshot includes shell Configuration; the candidate request changes feature states only. EnabledFeatures is merely the directly enabled candidate set, so it is not a final composition graph. The EF adapter reads root IConfiguration explicitly and composes both current and candidate feature states with shell Configuration, host defaults and public dependency expansion. It checks source-generation changes around preparation and refuses/retries unstable input. No feature constructors/configurators run. Do not infer complete participant applicability from the management catalog or its Features-only revision.

The default implementation in Elsa.Modularity.Nuplane preserves existing legacy context construction. Elsa.Modularity.EntityFramework supplies the single replacement implementing the bounded policy below. Core and Nuplane gain no EF dependency, resource DTO or provider engine reference. Declare replacement kind on the contract through the repository's existing metadata mechanism. Registration selects one implementation explicitly and detects duplicate implementations with a clear startup diagnostic; it is not an ordered collection of mutating preprocessors.

Invocation order is normative:

1. Existing request validation and secret restoration.
2. Mandatory context preparation and resource applicability preflight.
3. Only if preparation succeeds: existing ordinary activation guard loop.
4. Only after successful guards: existing authored save/refresh/reload path.

An ordinary guard is not sufficient: current guard execution continues after a refusal and a later persistence guard may perform database work. Preparation refusal returns immediately before all ordinary guards. It must also precede save, refresh and reload. It may read the current configuration and compose metadata without feature effects; it cannot invoke migration guards to discover whether a request is safe.

For a legacy editor write, refuse with the exact stable Reason prefix `[resource-managed-configuration]` if either the current or candidate final graph has an enabled enrolled consumer with effective resource selection, including invalid selected intent. Evaluate both states: disabling/removing a resource consumer must not bypass the current-state check, and newly enabling one must not bypass the candidate check. A valid definitions-only catalog or an inactive binding with no applicable consumer does not trigger refusal. Failed/ambiguous composition cannot be treated as proof of no applicability.

This token is text in the existing FeatureActivationRefusal.Reason and consequently the existing HTTP 409 errors.generalErrors message; no structured code field is added to that legacy envelope. Each reason begins `[resource-managed-configuration] Feature '<featureId>' uses resource-managed persistence. Edit authored configuration and reload the shell.` Render/escape the feature identity as data and keep it non-secret. Multiple-refusal exception formatting remains unchanged; tests assert the prefix in each emitted reason.

The refusal gives a safe instruction to edit authored configuration and use explicit reload for this first slice. It identifies affected feature/resource identities without displaying connection values or flattened effective settings. All current guards, legacy provider behavior and save semantics remain for requests with no applicable resource intent. Do not claim that this refusal supplies a full resource-management API.

## Verification obligations

- Startup and reload observe final globals and dependency-enabled consumers before any feature effect; reflection/discovery and the composer do not instantiate features.
- Absent/null/blank/reset/disabled cases preserve the authored decision, including code configuration and nested shell keys.
- A valid resource emits only the two target patches; authored documents and unrelated/unknown settings survive unchanged.
- Unsupported or conflicting resource input fails before feature binding/configurators/services, with canary secrets absent from errors.
- Changing a file and explicitly reloading changes the effective target; a failed candidate retains the previous active generation and data remains accessible there.
- Management tests instrument every ordinary guard and save/refresh/reload seam. For current-only, candidate-only and invalid applicable intent, every downstream count is zero.
- Definitions-only and wholly legacy edits continue through the same existing guards/store behavior. Secret restoration remains intact; resource preflight never exports restored values.
- Architecture tests prove no EF reference enters Core/Nuplane or the EF-free CLI/worker and no foundational resolver references workflow feature classes.
- Rebuilt host, database and tooling journeys in [quickstart.md](../quickstart.md) prove the supported shared layout before #1968 readiness is reported as delivered. The diagnostics split has its separate #1969 evidence gate.
