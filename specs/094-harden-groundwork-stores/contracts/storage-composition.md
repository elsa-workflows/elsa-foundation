# Contract: Host-Selected Groundwork Storage Composition

## Purpose

An application host selects one Groundwork provider and any supported combination of Groundwork-backed feature families. Before the host serves work, the composition pipeline gathers those features' durable requirements, validates one coherent target, applies the host's physical naming policy, and makes scoped store sessions available.

Core modules and their persistence contracts do not reference this contract or Groundwork.

## Contribution topology

The acyclic `Elsa.Persistence.Groundwork.Composition` implementation project owns only the shared source/event/context/snapshot contract. Domain Groundwork projects reference it but never `Unified`. The provider-specific `Unified` composition feature owns:

- a feature contract representing one manifest source and its stable feature identity;
- a named startup event carrying the mutable composition context;
- exactly one aggregating event handler that resolves all selected manifest sources;
- an immutable registry/snapshot populated before synchronous provider materialization;
- one `IPhysicalSchemaManifestSource` exposed to both runtime initialization and `Groundwork.Tool`.

`Unified` references Composition and removes hard-coded references to Runtime, Workflows Design,
Activities Design, and Publishing. Runtime, Workflows Design, Activities Design, Publishing, IAM,
Secrets, Distributed Runtime, and Diagnostics each register their own source when selected. The
in-memory `IRuntimeDiagnosticsSettingsStore` remains a separate linked #660 source/evidence
dependency; it does not stand in for the two durable diagnostics stores. This dependency shape is
acyclic and keeps domain behavior out of the composition contract.

Feature-specific Groundwork modules register a source; they do not materialize providers and do not each register an event handler for the fan-in. The app host selects a source by selecting that Groundwork feature. No selected source means no durable claim for that feature.

## Required source declaration

Each source supplies:

| Field | Rule |
|---|---|
| Feature identity | Stable and unique within the app. |
| Manifest | Provider-neutral Groundwork storage manifest owned by that feature. |
| Required public stores | Exact public contracts the source makes durable. |
| Required capabilities/routes | Derived from executable behavior, not options. |
| Scope classifications | Every unit classified scoped/global; privileged access remains an operation capability. |
| Topology prerequisites | Required transaction/feature constraints. |
| Coverage rows | Ledger identities advanced by this source. |

## Composition algorithm

1. Freeze selected feature identities in deterministic ordinal order.
2. Publish the named initialization event sequentially.
3. The one aggregator invokes every selected source and adds its declaration to the context.
4. Reject duplicate feature identities and missing required store declarations.
5. Union manifests with `StorageManifestComposition.Union`; surface unit collisions with both owning features.
6. Compile physical routes under the selected provider and host naming policy.
7. Validate provider capabilities and topology against active routes/transitions.
8. Produce an immutable composition snapshot and deterministic target fingerprint.
9. Make the snapshot available to runtime materialization and schema tooling.
10. Create store sessions with the declared tenant/global scope and separate ordinary/privileged operation access policy only after readiness succeeds.

Any failure stops startup before public store contracts become usable.

## Required feature combinations

The test matrix includes:

- runtime only;
- IAM only, with #644 authoritative identity composition;
- secrets only;
- distributed runtime plus runtime fencing/checkpoint authority;
- runtime + IAM + secrets + distributed runtime;
- the complete reference-server selection including design/publishing/diagnostics sibling manifests as they land.

The same selected feature set must compile on every mandatory provider without changing core contracts or domain behavior.

## 34-row host-selection evidence

The checked-in coverage ledger carries one digest-verified `host-selection-all34` composition record.
Its selected source identities cover the complete host composition. Runtime, IAM, Secrets, and
Distributed Runtime preserve the original 32-row denominator; the ratified 2026-07-25 amendment adds
the durable Structured Logs and OpenTelemetry rows contributed by `elsa-diagnostics`. Design,
Activities Design, and Publishing remain selected composition sources but do not claim ledger rows.

This record is composition evidence, not provider conformance evidence and not a new durability
authority. In particular:

- `iam-user`, `iam-role`, and `iam-external-identity` remain adapter-only links to authority `#644`;
- `runtime-diagnostics-settings` remains linked source/evidence owned by authority `#660`;
- each ledger entry retains its own delivery owner, status, and four-provider evidence obligations;
- the composition artifact proves only that host selection cannot silently omit or duplicate a current
  durable requirement.

The ledger validator compares the composition record with the exact 34-row denominator, checks the
external-authority links and their reviewed relationships against the row-level authority fields, and verifies the durable artifact at
`evidence/composition/host-selection-all34.json` by SHA-256 and payload equality. The earlier
`host-selection-all32` artifact remains immutable historical evidence.

## Scope/session acquisition

The provider materialization object owns static provider resources. A scoped session factory maps the provider-neutral Elsa persistence access context to:

- `DocumentStoreAccess.Scoped(StorageScope)` for ordinary tenant work;
- `DocumentStoreAccess.Global` only for units classified explicitly global;
- `DocumentStoreAccess.PrivilegedScoped`, `PrivilegedGlobal`, or `PrivilegedAcrossScopes` only after Elsa authorization supplies a named purpose/capability.

Sessions are immutable. A unit of work cannot mix scoped and global units. Privileged acquisition and outcome are observable, while metric labels remain low-cardinality and exclude tenant identifiers.

Logic-bearing store adapters, the manifest aggregator and handlers, access-context selectors, and session/unit-of-work consumers are scoped by default. Only static immutable provider resources may use a longer lifetime without a dedicated exception; registration/lifetime tests reject undocumented deviations and prove that tenant/access context and mutable operation state cannot cross request scopes.

Single-tenant hosts register a nonblank default persistence scope; the shipped default literal is `default`. Missing tenant context resolves to that scoped value, never to global access. If a domain operation carries an explicit tenant, it must match the active scope before provider I/O.

## Naming policy

The host may provide Groundwork's provider-neutral physical name policy and provider-specific identifier limits/quoting behavior. The order is:

1. feature default logical name;
2. host provider-neutral transformation (prefix, suffix, casing, or replacement);
3. provider renderer/normalizer for length, reserved words, quoting, and uniqueness;
4. deterministic collision validation.

Provider-specific renderers may make a transformed name legal but may not change which logical
storage unit it represents. Runtime and the separate CLI process construct the same deterministic
policy definition and must produce identical resolved-name evidence; they do not share an in-memory
policy instance.

## CLI/deployment contract

The repository pins `Groundwork.Tool` to the exact version used by all Groundwork packages. A host
supplies one public parameterless `GroundworkDeploymentSchemaManifestSource` subtype and registers the
same type through `AddGroundworkStorageComposition<TDeploymentSource>()`; that makes its selected
feature sources and host naming policy authoritative for runtime and the separate CLI process. The
shipped unified leaves use
`Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema`. Deployment
source construction must be deterministic and configuration-complete: all inputs that affect the
manifest or host naming policy are encoded by the selected source type, not supplied through
runtime-only mutable state. Deployment
pipelines can run:

```bash
dotnet groundwork validate \
  --manifest-assembly <built-composition-assembly> \
  --manifest-type <selected-schema-source> \
  --provider <sqlite|sqlserver|postgresql|mongodb> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork plan \
  --manifest-assembly <built-composition-assembly> \
  --manifest-type <selected-schema-source> \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork status \
  --manifest-assembly <built-composition-assembly> \
  --manifest-type <selected-schema-source> \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork apply \
  --manifest-assembly <built-composition-assembly> \
  --manifest-type <selected-schema-source> \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --safe \
  --output json
```

Runtime startup validates readiness but does not silently apply schema changes. Protected destructive/semantic changes require an exact retained plan fingerprint and exact operation approvals under Groundwork's locked apply protocol.

## Failure diagnostics

Composition diagnostics identify:

- selected feature and manifest source;
- storage unit/query/transition/capability;
- selected provider and topology requirement;
- duplicate/missing/incompatible owners where applicable;
- stable diagnostic identity and remediation category.

They never include connection strings, secrets, document payloads, or tenant identifiers as metric labels.

## Compatibility façade exit

`GroundworkUnifiedManifest.Create()` may temporarily forward to the new immutable composition for existing hosts. It must not retain a hard-coded list, create a second target fingerprint, or remain the schema-tool authority. The façade is removed when all callers use host-selected composition.
