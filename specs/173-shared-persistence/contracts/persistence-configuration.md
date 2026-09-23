# Persistence configuration contracts

Status: Integrated design review approved on 2026-09-23 for #1967. Published in PR #1973; implementation and runtime/database verification remain #1968/#1969 work.

Canonical decisions:

- [Authored persistence configuration and presence](../decisions/authored-persistence.md)
- [Explicit configuration context for resource-aware tooling](../decisions/tooling-configuration-context.md)
- [Resource and module selection in persistence tooling](../decisions/tooling-target-selection.md)
- [Verify the supplied connection against the selected resource](../decisions/tooling-target-verification.md)
- [Feature specification](../spec.md)
- [Research and decisions](../research.md)

The exact internal detached records are defined in [data-model.md](../data-model.md). This contract records how adapters obtain, validate, and consume those records. It deliberately does not add a second public DTO set.

## Resolver boundary

The persistence-owned resolver is side-effect free and internal to the first slice:

    internal interface IPersistenceResourceResolver
    {
        PersistenceResolutionResult Resolve(PersistenceResolutionInput input);
    }

It accepts the root resource catalog and root default, the selected shell default and feature bindings, explicit participant metadata, legacy target-field presence, and a detached source context. It applies this order:

    shell feature binding -> shell default -> root default -> legacy feature configuration

The resolver returns per-participant Provider and ConnectionName materialization, selection provenance, and redacted refusal or unresolved evidence. Provider and ConnectionName are one atomic resource target. A resource definition without both nonblank fields is invalid when selected. Definitions alone do not select a resource.

The resolver does not read IConfiguration, instantiate features, invoke configurators, resolve a connection string, open a database, change feature enablement, change schema or pooling, apply migration policy, write authored JSON, or mutate a shell. Inherited values are runtime or tooling materialization only; they are never written back as legacy feature settings.

## Cross-assembly preparation boundary

Keep the resolver and its detached model internal to Elsa.Persistence.EntityFramework. Its runtime/management callers in Elsa.Modularity.EntityFramework use one concrete public facade in `Elsa.Persistence.EntityFramework.ResourceResolution`; no InternalsVisibleTo or new replacement interface is needed:

```csharp
public static class EfPersistencePreparation
{
    public static EfPersistencePreparationResult Prepare(
        ShellSettingsPreparationContext context,
        IConfiguration rootConfiguration);
}

public sealed record EfPersistencePreparationResult(
    ShellSettingsPreparationResult Patch,
    bool HasApplicableResource,
    IReadOnlyList<EfPersistenceApplicabilityItem> Participants,
    IReadOnlyList<string> RefusalCodes,
    IReadOnlyList<string> UnresolvedCodes);

public sealed record EfPersistenceApplicabilityItem(
    string FeatureId, string? ResourceName, string SelectionKind);
```

The context/result types are the public `CShells.Lifecycle` types delivered in preview.157; root configuration is explicitly supplied by the caller. The facade builds detached inputs, invokes the internal resolver and returns only existing scalar patches plus redacted applicability. HasApplicableResource includes malformed selected intent; definitions-only and unknown unenrolled bindings are not applicable. RefusalCodes nonempty requires an empty patch. UnresolvedCodes never mean a readiness pass. Resource-selected participants carry source selection kind even when their selected name is malformed; do not infer applicability from nonnull ResourceName alone.

The runtime CShells adapter returns Patch only after checking refusals. The management adapter composes complete current/candidate contexts using the shared host composer and public CShells APIs, calls this facade for each and refuses applicable intent before ordinary guards. It must not manufacture an incomplete context from directly enabled IDs. Root/raw configuration must correspond to the same candidate representation when presence is inspected; never combine candidate final settings with stale authored fields from another source generation.

The tooling adapter is already in the persistence assembly and can consume internal resolution/evidence directly; it does not serialize the public facade or expose the internal graph. This boundary requires no Modularity dependency in Persistence. Source inspection confirmed ShellSettingsPreparationContext and ShellFeaturePreparationDescriptor have public constructors for complete detached contexts. Integration tests must compare those contexts to runtime composition rather than trusting a hand-written graph.

## Presence, source, and precedence

The data model uses one presence-aware value for resource names, provider names, defaults, and bindings. The legacy target adapter uses presence and provenance only for Provider, ConnectionName, and ConnectionString. In particular, the ConnectionString field has no string value in any resolver input or output.

The adapter combines the final CShells settings-preparation map with a narrow selected-shell IConfiguration read for those three legacy target keys. It uses child enumeration or provider TryGet and never IConfigurationSection.Exists(), so absent and explicit null remain distinguishable. It supports object-map features and array entries with Name, direct settings, or a Settings wrapper. CShells shape validation remains authoritative.

Non-null final code/configuration target keys count as authored. CLR property initializers do not. FeatureSettingResetIds suppress lower-priority raw fields, and the final dependency-expanded graph determines applicability, including a dependency that reintroduces a feature. A directly disabled feature remains inactive.

The source context records mode, shell, checked source categories, and whether operator-supplied external environment input was included. It never carries raw paths, snapshots, or secrets. Runtime uses the host source chain. Tooling accepts only workbench-json-v1 or the explicit workbench-json-environment-v1 mode. The latter means worker-inherited environment supplied by the operator; it does not claim parity with an independently running host. Management uses the restored authored snapshot and source context available to that operation.

Selection semantics are:

- Shell binding wins over shell default.
- Shell default wins over root default.
- Root default wins over legacy feature configuration.
- Root feature bindings and shell-local resource catalogs are unsupported in this slice.
- Deleting the owning key reveals the next lower-priority value.
- Null, empty, blank, wrong-type, unknown, or incomplete selected values refuse; null is never deletion.
- New resource and connection-reference names use the conservative identifier-like rule in [the authored-configuration decision](../decisions/authored-persistence.md#configuration-surface); boolean/numeric-looking scalar tokens are reserved because `IConfiguration` erases their original JSON token type. Legacy feature values retain their existing syntax.

## Enrollment and existing metadata

Enrollment is explicit on the owning feature class:

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EfPersistenceResourceParticipantAttribute : Attribute;

The marker is combined with existing metadata without constructing feature classes:

- ShellFeatureAttribute supplies stable feature identity.
- UsesEfModuleAttribute maps the feature to canonical EF modules.
- EfFeatureModuleUsage reports module usage and whether the feature declares Provider.
- EfModuleDescriptor supplies context identity, provider-derived context support, migration history, default connection name, and dependencies.

The marker is the enrollment gate. Property-name resemblance, assembly hardcoding, and feature constructor defaults are not enrollment mechanisms. An applicable participant with an opaque configurator refuses before that configurator is invoked.

The 13 supported identities and their module/context ownership are canonical in [specification § Normative supported participants and constraints](../spec.md#normative-supported-participants-and-constraints). Dashboard readers, Identity IAM and provider configuration, Secrets, distributed contexts, Elsa 3 import, OpenIddict, private stores, and unknown/custom consumers remain outside automatic enrollment.

## Runtime and reload adapter

The runtime adapter runs at the CShells settings-preparation seam:

1. Receive the final dependency-expanded shell composition before feature construction.
2. Obtain root resources/default and shell default/bindings.
3. Build narrow legacy target presence from the final map and selected-shell IConfiguration.
4. Invoke the internal resolver.
5. Apply only Provider and ConnectionName scalar patches before feature binding.

Schema, pooling, migration policy, secrets, unknown settings, disabled/reset state, and unrelated fields remain owned by their existing paths. A refusal occurs before feature constructors, configurators, service registration, or database activity. The adapter runs again for every shell generation and reload; it does not replace CShells parsing or feature APIs.

## Management and tooling boundaries

The existing FeatureManagementService request remains the legacy feature editor. For the first slice, an enabled enrolled participant whose effective selection is a named resource causes refusal before activation guards, save, catalog refresh, or shell reload. The operation directs the operator to edit the authored resource/default/binding configuration and reload explicitly. A legacy request with no applicable resource keeps its existing behavior. It must not persist generated inherited Provider or ConnectionName values.

Any future resource-aware management write requires current source/resource drift checks, authored-intent preservation, and truthful saved-versus-activated reporting. The Features-only revision is not a resource/source concurrency token. The exact preparation signature and refusal order are in [runtime-management.md](runtime-management.md); no new editor request is introduced.

The CLI front end and worker stay EF-free. The host-owned tooling declaration is:

    EfToolingShellDefaultsAttribute

on the host assembly, carrying its composer Type, with the composer implementing:

    IEfToolingShellDefaults.Configure(ShellBuilder, IConfiguration)

The host must enforce the same declaration at runtime. The [tooling contract](tooling.md) defines the distinct private-worker, context, operation and manifest versions; the legacy host entry point remains v1. The context is an opaque host-owned snapshot and never includes the actual env/stdin connection. The worker must refuse old hosts or partial capability negotiation rather than fall back to provider-only projection.

Offline list/plan/script uses resource membership and selected module scope without expected-connection lookup. Live apply/validate/post-migrate obtains the actual connection only through existing env/stdin input, looks up the expected named connection inside the explicit host context, and strictly compares them before creating a DbContext or opening a database. No connection value, hash, raw path, snapshot, or driver/reflection exception reaches output or process arguments.

## EF validation and connection sidecars

The pure resolver is not the EF validator. EF-owned validation consumes the resolved participant plan and existing module metadata for:

- provider engine availability and provider-derived context support;
- per-feature provider agreement;
- Runtime participants sharing RuntimeDbContext with compatible provider, connection, schema, and pooling options;
- exact Activities Design and Workflows Design shared-transaction requirements;
- ordered Runtime, Activities Design, then Publishing operations without inventing a blanket three-context transaction;
- Structured Logs and OpenTelemetry local transaction ownership;
- provider-specific schema rules and existing migration policy.

EfConnectionDefaults.ResolveConnectionString remains the runtime/legacy connection sidecar. It runs only after resource materialization inside the trusted host. Tooling expected-value lookup is a separate trusted sidecar. Neither sidecar is part of the pure resolver, and no resolved secret returns through the resolver or tooling protocol.

Resource names and aliases express reference-level grouping only; they do not prove physical database equality or transaction affinity. Different references are not automatically incompatible. Physical equality is unverified offline and checked strictly only by the trusted live operation when required.

The shared Runtime, Workflows Design, Activities Design, and Publishing layout and the separate diagnostics layout remain configuration candidates. Diagnostics split proof and live host/database evidence remain owned by #1968 and #1969.

## Refusal and preservation

Refuse before side effects for missing, blank, null, wrong-type, unknown, or incomplete selected values; applicable resource mode combined with authored Provider, ConnectionName, or ConnectionString; opaque participant configurators; unresolved or unsupported ownership needed by a requested executable target; incompatible provider/context/module selection; schema, pooling, transaction, expected-connection, or capability disagreement.

Preserve unknown resources and fields when not selected, unknown feature IDs/settings, disabled/reset state, unrelated authored feature fields, host-owned/private stores, and sources outside the selected context. A source-collection race refuses or retries the candidate snapshot; it does not claim an atomic configuration transaction. No refusal may disclose a secret or cause an unintended migration probe, context construction, database access, save, refresh, or reload.

Stable configuration refusal codes are `resource-selection-invalid` (null/blank/wrong-type selection), `resource-not-found`, `resource-definition-invalid` (selected incomplete/unsupported resource shape), `resource-legacy-conflict`, `resource-configurator-unsupported`, `resource-required-feature-disabled`, `resource-context-conflict`, and `resource-source-changed`. Unknown unenrolled bindings use unresolved code `resource-participant-unenrolled`; unsupported authored scopes use `resource-scope-unsupported`. They remain preserved and do not by themselves activate or refuse a legacy composition. If requested executable scope requires ownership that cannot be established, refuse `resource-ownership-unresolved`. Refusal identity fields follow the detached model; safe adapters format these codes without raw values. Tooling-specific and management-specific codes are defined in their own contracts.

## Functional-requirement mapping

The contract maps to every requirement in [specification § Functional Requirements](../spec.md#functional-requirements):

| Requirement | Contract consequence |
|---|---|
| FR-001 | A resource has atomic Provider and ConnectionName fields. |
| FR-002 | Binding, shell default, root default, then legacy precedence; definitions alone do not select. |
| FR-003 | The explicit marker and existing EF metadata enroll only the canonical 13 identities. |
| FR-004 | Diagnostics participants may bind to a secondary resource; proof remains #1969. |
| FR-005 | Owning-key deletion restores inheritance; null/empty/unknown refuses. |
| FR-006 | Authored legacy Provider, ConnectionName, or ConnectionString conflicts with applicable resource mode. |
| FR-007 | No applicable resource leaves existing provider, connection, schema, pooling, policy, and precedence unchanged. |
| FR-008 | Missing, incomplete, or unsupported selection refuses before unintended persistence activity. |
| FR-009 | Presence, source, false/zero/null/empty, reset, and absence remain distinct at the adapter boundary. |
| FR-010 | Runtime, reload, management inspection, and tooling use the same detached resolver semantics. |
| FR-011 | Results identify source context and checked categories; unavailable external parity remains unverified. |
| FR-012 | Resource selection does not own schema, pooling, or migration permission. |
| FR-013 | EF-owned validation enforces context and operation constraints. |
| FR-014 | Resolver and tooling evidence contain no connection values or reversible representations. |
| FR-015 | Provider remains authoritative; tooling preserves env/stdin actual input, module scope, and old-host refusal. |
| FR-016 | Resolution performs no package install, database access, save, or activation. |
| FR-017 | Legacy management refuses resource-mode mutation before guards and mutation. |
| FR-018 | Future accepted writes require source drift checks, authored preservation, and truthful activation state. |
| FR-019 | Startup and reload recompute before feature binding on every generation. |
| FR-020 | Unknown authored data survives and unknown ownership remains unresolved. |
| FR-021 | The linked specification, decisions, and verification inventory define required evidence. |

Implementation, package pinning, runtime host proof, diagnostics split proof, and integrated verification remain implementation gates.
