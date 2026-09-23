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

**Decision:** Add the missing preparation seam upstream in [CShells #134](https://github.com/valence-works/cshells/issues/134), then pin its published package before #1968 runtime integration. Independent architecture review approved the prerequisite contract. The hook runs after global/default composition AND dependency expansion, before any feature construction/binding/configurator/service registration. It receives requested, implicit, unknown and ordered effective identities with final settings. CShells enforces that preparation changes configuration only; it cannot change identity, feature membership/order or configurator delegates.

**Rationale:** Both the pinned version and current canonical CShells main `e5c811402625b39c1a4348bda2c0615400ab0f09` have the same gap. Global callbacks can add participants after an external wrapper; dependency expansion adds more later. Opaque configurators can overwrite bound values. The actual Workbench callback currently selects only non-persistence behavior, but that is insufficient as a general contract.

**Elsa rule:** The resolver includes globally selected and dependency-enabled participants. An opaque configurator on a participant with an applicable resource selection refuses by stable feature identity before invocation, because its authored target intent cannot be reconstructed. Configurators on legacy or unrelated features remain unchanged. Resource definitions alone do not trigger this refusal.

**Alternatives rejected:** A frozen overlay becomes stale; an early provider wrapper misses final participants; internal descriptor replacement copies framework internals; declaring every custom composition ready from today's Workbench files would overclaim support. The upstream hook is generic; persistence semantics stay in Elsa.

**Reload:** Host source providers may refresh configuration, but rebuilding an active shell requires explicit registry reload. Each generation must prepare a fresh snapshot. Required upstream tests prove global/implicit participation, binding consumption, zero feature-side effects on refusal, invariant enforcement and recomputation. These tests have not yet run; #134 owns delivery and publication.

## R3. Existing feature management with file-authored resources

**Decision for the first slice:** Keep resource/default/binding authoring in configuration files. The existing feature-management request is not extended into a resource editor. Legacy writes retain existing behavior. If either the current or candidate composition applies a resource to an enabled enrolled consumer, the legacy management write refuses before the existing activation-guard loop, save, refresh or reload. The error directs the operator to edit the authored composition and explicitly reload after validation. Resource definitions without an applicable selection do not trigger refusal.

**Rationale:** Current revisions cover Features only, and current writes are not atomic compare-and-swap. Safe resource-aware management acceptance needs source-context concurrency plus saved-versus-activated reporting. Those remain required program outcomes under #1964; treating the existing request/revision as sufficient would create a false safety claim. File/shell reload and the runtime resolver remain mandatory for #1968.

**Integration:** Add a single replacement context-preparation service in Modularity's existing context-construction seam, with a legacy pass-through default and an EF adapter. Invoke it after secret restoration/request validation and before any guard. Do not register resource refusal as an ordinary guard: all ordinary guards currently run even after a refusal, so a later migration guard could probe an unintended default target. Save always receives authored intent, never a generated effective request. No EF dependency enters Modularity.Core or Nuplane.

**Next program gate:** #1964 must deliver resource-aware mutation with an explicit remove operation, current-context verification, writer/conflict scope and truthful saved/activated outcome. This first-slice refusal is not completion of management/builder operations.

**Alternatives:** Accepting writes with the Features-only revision is unsafe. Expanding this story into a generic transaction/apply system would skip the existing operational epic. A safe management adapter can replace the refusal implementation once the operation contract is specified and verified.

## R4. Host tooling and target identity

**Required boundary:** Preserve ADR 0076's EF-free front end, host dependency closure, authoritative provider, explicit file context and environment/stdin secrets. Extend the protocol with deliberate version/capability negotiation before the provider-only projection discards resource intent. A live invocation currently receives one connection, so a multi-target layout must select and validate each target's module set independently.

**Owner decision pending:** [Strict target verification proposal](decisions/tooling-target-verification.md) asks whether resource-aware tooling may read the expected configured connection solely to compare the explicit env/stdin live value. Do not adopt that amendment or claim connection-value parity before the decision.

**Remaining design after decision:** Choose the narrow explicit input that reaches the host-owned resolver without shipping secret values in public plans or assuming the tool's environment is the host environment. Define configuration checks versus live target checks, and how unsupported old hosts refuse. Review any ADR extension before implementation.

## R5. Scope and evidence

**Decision direction:** First deliver shared PostgreSQL for the #1965 reviewed Runtime/Workflows Design/Activities Design/Publishing participants on a rebuilt Workbench; then prove both diagnostics consumers on a second target. Preserve legacy behavior and host-owned exceptions. This is an incremental program milestone, not a replacement for profiles, developer workflows, builder UX or operations.

**Required evidence:** Real design/publish/execute/restart data, migration histories and tooling target agreement; source/presence conflict matrix; successful recomputation on reload; secret redaction and no-side-effect refusals. Existing passing component tests are regression evidence only.

**Status:** #1968 and #1969 remain blocked. R2 has a reviewed upstream implementation prerequisite; R3 has a bounded safe first-slice decision. R4 awaits the owner decision. Complete plan/contracts/tasks and review before implementation readiness. CShells #134 can proceed independently of R4.
