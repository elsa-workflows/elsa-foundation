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

**Reload:** Host source providers may refresh configuration, but rebuilding an active shell requires explicit registry reload. Each generation must prepare a fresh snapshot. Required upstream tests prove global/implicit participation, binding consumption, zero feature-side effects on refusal, invariant enforcement and recomputation. The upstream implementation is independently reviewed in [CShells PR #135](https://github.com/valence-works/cshells/pull/135), commit `d4b11b856159236d9493b19481200002b70d932b`. Its local verification passed 679 Release tests, including 11 focused preparation cases, with a cancellation mutation check. PR CI and independent review passed; Copilot was unavailable because its monthly review limit had been reached. The PR merged at `ba501785eea6d0807dd687ee79260d9a40c75140`. The [post-merge package workflow](https://github.com/valence-works/cshells/actions/runs/35895766960) succeeded and published `0.0.30-preview.157` to Feedz. An isolated package-only net10.0 consumer restored CShells and CShells.Abstractions, verified both nuspec commit identities, activated the hook, and confirmed root-only registration. [#134 is closed with evidence](https://github.com/valence-works/cshells/issues/134#issuecomment-5799691304); Elsa has not pinned or integrated the package yet. The final API exposes a detached scalar shell-configuration view plus set/remove changes; untouched typed configuration is preserved. Root configuration fallback is deliberately separate and must be supplied explicitly by the Elsa adapter when required.

## R3. Existing feature management with file-authored resources

**Decision for the first slice:** Keep resource/default/binding authoring in configuration files. The existing feature-management request is not extended into a resource editor. Legacy writes retain existing behavior. If either the current or candidate composition applies a resource to an enabled enrolled consumer, the legacy management write refuses before the existing activation-guard loop, save, refresh or reload. The error directs the operator to edit the authored composition and explicitly reload after validation. Resource definitions without an applicable selection do not trigger refusal.

**Rationale:** Current revisions cover Features only, and current writes are not atomic compare-and-swap. Safe resource-aware management acceptance needs source-context concurrency plus saved-versus-activated reporting. Those remain required program outcomes under #1964; treating the existing request/revision as sufficient would create a false safety claim. File/shell reload and the runtime resolver remain mandatory for #1968.

**Integration:** Add a single replacement context-preparation service in Modularity's existing context-construction seam, with a legacy pass-through default and an EF adapter. Invoke it after secret restoration/request validation and before any guard. Do not register resource refusal as an ordinary guard: all ordinary guards currently run even after a refusal, so a later migration guard could probe an unintended default target. Save always receives authored intent, never a generated effective request. No EF dependency enters Modularity.Core or Nuplane.

**Next program gate:** #1964 must deliver resource-aware mutation with an explicit remove operation, current-context verification, writer/conflict scope and truthful saved/activated outcome. This first-slice refusal is not completion of management/builder operations.

**Alternatives:** Accepting writes with the Features-only revision is unsafe. Expanding this story into a generic transaction/apply system would skip the existing operational epic. A safe management adapter can replace the refusal implementation once the operation contract is specified and verified.

## R4. Host tooling and target identity

**Required boundary:** Preserve ADR 0076's EF-free front end, host dependency closure, authoritative provider, explicit file context and environment/stdin secrets. Extend the protocol with deliberate version/capability negotiation before the provider-only projection discards resource intent. A live invocation currently receives one connection, so a multi-target layout must select and validate each target's module set independently.

**Owner-approved direction (2026-09-23):** Adopt [strict target verification](decisions/tooling-target-verification.md), provided it remains bounded. Read the expected named connection inside the explicitly selected host context only to compare it with the supplied env/stdin value. Keep the actual database input env/stdin-only, refuse before database access on unresolved/mismatched values, and never emit either value. This adds a lookup and the existing equality check, not a generic secret provider or database probe. Runtime proof, protocol design and ADR review are still required.

**Remaining design:** Choose the narrow explicit input that reaches the host-owned resolver without shipping secret values in public plans or assuming the tool's environment is the host environment. Define configuration checks versus live target checks, and how unsupported old hosts refuse. Review any ADR extension before implementation.

## R5. Scope and evidence

**Decision direction:** First deliver shared PostgreSQL for the #1965 reviewed Runtime/Workflows Design/Activities Design/Publishing participants on a rebuilt Workbench; then prove both diagnostics consumers on a second target. Preserve legacy behavior and host-owned exceptions. This is an incremental program milestone, not a replacement for profiles, developer workflows, builder UX or operations.

**Required evidence:** Real design/publish/execute/restart data, migration histories and tooling target agreement; source/presence conflict matrix; successful recomputation on reload; secret redaction and no-side-effect refusals. Existing passing component tests are regression evidence only.

**Status:** #1968 and #1969 remain blocked. R2 has a reviewed upstream implementation prerequisite; R3 has a bounded safe first-slice decision. R4 has owner approval for the bounded strict check; concrete context/protocol contracts remain to be reviewed. CShells #134 is delivered and #1967 is the single active delivery item. Complete plan/contracts/tasks and review before implementation readiness.

## R6. Explicit tooling source context: engineering findings to resolve in the contract

The source investigation confirms that current `ElsaCli.Selectors.Resolve` / `ShellConfiguration.Read` project shell files to feature/provider selections before the worker boundary. Resource definitions, bindings and connection references do not survive that projection. `WorkerRequest` already has host-directory/environment/shell selectors; `ToolingEntryPoint` already checks host capabilities by reflection before sending newer fields. Module selection and provider agreement are host-owned and must remain so.

For Workbench, the reproducible file order is appsettings.json, its environment overlay, shells.json, and its environment overlay. Runtime environment-variable and command-line providers are subsequently re-added by Workbench; current file-only CLI readers do not observe the independently running host's overrides. `HostAppSettings.Read` and `ShellConfiguration.Read` each compose only their own pair of files. Neither alone supplies the complete file context for an expected named connection.

The minimal candidate is a versioned explicit file context plus one host-owned lookup and strict comparison. It must not infer live environment parity from the tool process. A missing expected value refuses before database access. Supporting external secret providers or different host source orders is outside this candidate and must be marked unsupported/unverified rather than silently approximated. These are engineering findings, not final wire fields or approval to narrow the whole program to file-only configuration.

Before closing research, review exactly how the selected directory reaches the host-side adapter (the worker field alone is not proof that the reflected host request receives it), preserve one selected module set per supplied connection, and distinguish reference/configuration checks from observed runtime evidence. An older host must refuse a resource-context request before silently dropping the new field. Keep raw paths and connection values out of public host facts and exported plans.

Source pointers: `src/apps/Elsa.Workbench/Program.cs`; `src/essentials/Cli/ShellConfiguration.cs`; `src/essentials/Cli/Worker/HostAppSettings.cs`; `WorkerContract.cs`; `WorkerRunner.cs`; `ToolingEntryPoint.cs`; `src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs`; `EfToolingHost.cs`; `EfProviderAgreement.cs`. The existing Workbench configuration, worker launch, capability-refusal, and CLI redaction tests are evidence starting points, not new-mode proof.
