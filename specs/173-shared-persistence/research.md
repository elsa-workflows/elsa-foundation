# Shared persistence research and decisions

Status: planning input in progress. This does not approve implementation. The reviewed discovery records are [persistence boundaries](../../docs/reports/runtime-composition/persistence-boundaries.md) and [effective configuration](../../docs/reports/runtime-composition/effective-configuration.md).

## R1. Resolve before feature binding

**Decision direction:** Use one persistence-owned pure resolver with runtime, management and host-tooling adapters. Preserve raw presence and explicit source context. EF-owned participant metadata supplies constraints; retain module-owned contexts.

**Rationale:** The production provider-agreement probe demonstrates that absent feature providers become SQLite before agreement checks unless the selected resource has already been materialized. Authored target fields must be distinguished from initialized defaults.

**Alternatives:** Activation guards alone cannot cover startup. Repeating resolution inside each feature loses shared context and makes tooling disagree. A generic settings framework would exceed the approved bounded slice.

**Status:** Ownership reviewed in #1966. Exact input/output contracts and packaging still to be designed.

## R2. CShells lifecycle integration

Elsa pins CShells `0.0.29-preview.147`; its package repository commit is `6dd505d2ee0d40fb222b079e81acca8bdbab17c5`.

The pinned [ConfigurationShellBlueprint](https://github.com/sfmskywalker/cshells/blob/6dd505d2ee0d40fb222b079e81acca8bdbab17c5/src/CShells/Lifecycle/Blueprints/ConfigurationShellBlueprint.cs) creates fresh settings from its bound configuration section on every ComposeAsync call. The [registry](https://github.com/sfmskywalker/cshells/blob/6dd505d2ee0d40fb222b079e81acca8bdbab17c5/src/CShells/Lifecycle/ShellRegistry.cs) obtains and composes a blueprint before building a shell generation; reload failure can retain the old active generation. These are upstream source observations, not an executed Elsa adapter proof.

**Candidate:** Register one Elsa blueprint-provider adapter that delegates to the stock configuration provider and wraps composition before feature binding. Preserve the provided manager and metadata; recompute from current inputs on every composition. Do not register two competing external providers.

**Ordering finding:** The outer configured blueprint merges ConfigureAllShells defaults after the external provider returns. Ordinary configuration defaults lose to shell-specific values, but global feature selections can add participants after an inner resolver, and feature configurator actions run after property binding and can override the target. An external wrapper alone therefore does not establish safe behavior for arbitrary code-configured persistence. See pinned [configured provider](https://github.com/sfmskywalker/cshells/blob/6dd505d2ee0d40fb222b079e81acca8bdbab17c5/src/CShells/Lifecycle/Providers/ConfiguredShellBlueprintProvider.cs).

**Remaining gate:** Establish a safe final ordering or an explicit, detectable unsupported-composition refusal. Inspect the supported Workbench's actual defaults and preserve legacy code configuration. Do not use internal service-descriptor surgery as an assumed stable hook. A broader hook may require upstream work if final merged settings cannot be observed through a supported seam.

Configuration source reload does not itself rebuild an active shell. Explicit registry reload recomposes current source values; automatic host orchestration is separate. Verify the chosen order and explicit reload behavior with a focused executable adapter test during delivery.

**Alternatives:** A frozen materialized configuration root would become stale. An upstream hook is unnecessary if the existing public provider seam proves adequate, but this is not yet the final registration decision.

## R3. Existing feature management with file-authored resources

**Candidate:** Keep the current feature apply request focused on feature enablement/options. Resource definitions, shell default and bindings may be authored in the existing shell Configuration area for the first slice. The JSON store already rewrites only Features, preserving that separate area. The eventual builder requires a deliberate composition editing contract under later program work.

**Required safety:** Resolve the effective candidate after secret restoration and before guards; save authored feature settings only. An opaque revision must account for relevant authored resource state and checked external context, not only Features. Recheck before save. Report saved-but-not-activated outcomes distinctly; the current reload count of zero is insufficient as a success signal.

**Open design:** Decide the revision/context ownership and how a stale context can be detected without leaking secrets. The current direct file write is not atomic compare-and-swap; determine the supported writer scope and prove the chosen conflict behavior. Unsupported/unobservable external state cannot be treated as verified. Do not claim the broader durable operation orchestrator exists.

**Alternative:** Expanding FeatureApplyRequest into a generic composition document would prematurely mix authoring and operations. A future versioned composition endpoint can support explicit remove versus null and resource editing without flattening inheritance.

## R4. Host tooling and target identity

**Required boundary:** Preserve ADR 0076's EF-free front end, host dependency closure, authoritative provider, explicit file context and environment/stdin secrets. Extend the protocol with deliberate version/capability negotiation before the provider-only projection discards resource intent. A live invocation currently receives one connection, so a multi-target layout must select and validate each target's module set independently.

**Open design:** Choose the narrow explicit input that reaches the host-owned resolver without shipping secret values in public plans or assuming the tool's environment is the host environment. Define configuration checks versus live target checks, and how unsupported old hosts refuse. Review any ADR extension before implementation.

## R5. Scope and evidence

**Decision direction:** First deliver shared PostgreSQL for the #1965 reviewed Runtime/Workflows Design/Activities Design/Publishing participants on a rebuilt Workbench; then prove both diagnostics consumers on a second target. Preserve legacy behavior and host-owned exceptions. This is an incremental program milestone, not a replacement for profiles, developer workflows, builder UX or operations.

**Required evidence:** Real design/publish/execute/restart data, migration histories and tooling target agreement; source/presence conflict matrix; successful recomputation on reload; secret redaction and no-side-effect refusals. Existing passing component tests are regression evidence only.

**Status:** #1968 and #1969 remain blocked. Resolve R2-R4, write plan/contracts/tasks, review against spec acceptance, then refine the story work before readiness.
