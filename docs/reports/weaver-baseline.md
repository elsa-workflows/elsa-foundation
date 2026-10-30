# Weaver across Studio: M0 baseline

**Date:** 29 October 2026. **Owner:** [#3436](https://github.com/elsa-workflows/elsa-foundation/issues/3436),
Program [#3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), M0 #3429.
**Source inspected:** `5d64edddacd492d213de303dcd0cc040a044ae0c`.
This report records observed availability, source wiring and the finite starting coverage matrix.
The Program issues own delivery; this report is evidence rather than a separate queue or accepted API.

## Observed availability

The signed-in normal Studio at `https://localhost:7030` reproduces the unavailable Weaver dock and
disabled composer. The backend is `https://localhost:7243`. An authenticated browser navigation to
`/_elsa/agent/bootstrap` returns a data envelope with `enabled:false`, `providerStatus:unavailable` and:

| Field | Observed value |
|---|---|
| Provider | `github-copilot` |
| Provider availability | `false` |
| Diagnostic status code | `missing-auth` |
| Authentication mode | `none` |
| SDK assembly version | `1.0.4.0` |
| Default / maximum autonomy | `auto-read-only` / `full-auto` |

The provider's normalized explanation states that no backend-owned token, configured environment
token, logged-in-user mode or external runtime URL is configured. This is the demonstrated cause of
the reported unavailable state. It is not an SDK transport failure, a missing Studio module or proof
that the user lacks Copilot entitlement. [Readiness](../../Extensions/Agent/src/GitHubCopilot/Services/GitHubCopilotAgentProvider.cs)
and [configuration modes](../../Extensions/Agent/src/GitHubCopilot/README.md) define the supported setup.
The source default enables Copilot and selects `COPILOT_GITHUB_TOKEN`; enabling a feature does not
provision provider access. Do not manufacture credentials, change the provider default or restart
developer-owned hosts to make the status appear healthy.

The running checkout reports `6ad81f7c7a825bce5d5f8cbc4214de40d92e0d87`; its tracked source is clean.
Its only reported untracked paths are Studio Nuplane state. The Agent/Weaver and shared agent/dock
source paths have no diff between that commit and the inspected baseline. This is a source comparison,
not a claim that checkout HEAD identifies every loaded binary. Loaded Workbench/Agent assembly file
identities were recorded separately:

| Assembly | SHA-256 |
|---|---|
| `Elsa.Workbench.dll` | `2b77c95498b7f683d452424683795642811f2c0ea0d77eee0caa34bba47e78c9` |
| `Elsa.Agent.Core.dll` | `bb6d53eb9b6e47d98fded983532f0bb0698c4c1d9475197dcff70ed596627a23` |
| `Elsa.Agent.Api.dll` | `7df1b79840eec4148278a5bfd5cb23fdb9f3aeae5a40b633339be0df5bd58de0` |
| `Elsa.Agent.Workflows.dll` | `44979155272867d188f5da9d99bef10b80aa46e7e8ea4e77dcc673382338d92b` |
| `Elsa.Agent.GitHubCopilot.dll` | `a5a2c90a9a72c54f1fa1d2867bcbac226b1b963dd8eb97875ce2580273bb39bb` |

Private local evidence contains the normalized bootstrap response (`m0-live-bootstrap.json`,
SHA-256 `1bab8b529e00a112a95036dc7b17a8db3bdd2b37b41a31af7358f6925c403735`), module load text
(`m0-live-modules.txt`, `cfad357fdcbef11f04173f7f3088f23cf7ce5610a127ac97bcc21d83ab71eb6d`), and the reproduced
dock screenshot (`m0-normal-studio.png`, `6d01512f2c964d8046d39bfa7870e43cf401994bb4ea5c985b6f0b996ef68152`).
No credentials, cookies, tokens or provider-state files were extracted. An initial attempt with the
committed demo login was rejected; the owner signed in interactively before these observations.

## Composed context inventory

The running `/diagnostics/modules` view reports 14 loaded modules: Attention, Connections,
ConsoleStream, Dashboard, OpenTelemetry, StructuredLogs, ETL, JavaScript and Liquid expression editors,
FeatureManagement, Secrets, Weaver.Workflows, Workflows and Workflows.Dashboard. ExtensionBuilder is
discovered but disabled. The source shell also configures a Weather sample, which is not in the live
loaded-module list. Host-owned settings and theme surfaces are additional contexts. No user/role/tenant
administration client module was found in the current tracked composition.

The table's final column is the **delivery target**, not current Weaver acceptance. Existing module
routes and domain APIs establish an integration opportunity, not a working assistant journey. Each
M3/M4 slice must refine and execute its relevant positive, denied, stale and unavailable cases.

| Context / owner | Current surface or domain seam | Delivery target |
|---|---|---|
| Home: Dashboard, Attention, Workflows.Dashboard | `/`, `/dashboard`, `/overview`; attention items and workflow dashboard APIs | Explain visible widget snapshots, filters, freshness and unavailable data; module-owned supported preferences |
| Definitions list: Workflows | `/workflows/definitions`; `elsa.api.workflow-design` definitions/folders/drafts/versions | Preserve search/folder/status/page/selection; read exact resources and propose supported lifecycle changes |
| New workflow: Workflows / Weaver.Workflows | Creation dialog, intent/root/name/description, real activity catalog | Carry caller attachment; return typed metadata suggestions or reviewed creation |
| Workflow draft: Workflows | Open editor, selected/inspected activity, nested graph, unsaved state and validation | Grounded explain/troubleshoot; atomic visible working-state edits, validation, dirty state and whole-batch undo |
| Expressions: JavaScript / Liquid | Inline/expanded editor; `expressions.tooling.v1` metadata/source/diagnostics | Language-owned guidance and exact expression edits without runtime evaluation or live-value capture |
| Activity definitions: Workflows | `/workflows/activity-definitions`, import/upgrades and draft editor; `elsa.api.activity-design` | Exact catalog/version/dependency inspection and supported draft/import/upgrade proposals |
| Executables: Workflows | `/workflows/executables` and artifact detail; runtime executable/provenance API | Inspect immutable compiled structure and selected source reference; dispatch remains a distinct authorized effect |
| Runs: Workflows | `/workflows/instances`, exact run detail and attached Test Run inspector | Carry exact run/activity-execution/incident/scope; authorized evidence and supported wait/stop/recovery operations |
| Runtime evidence: Workflows | `/workflows/runtime-diagnostics`; runtime-diagnostics capability relation | Explain capture policy and exact proposed configuration differences |
| Activity availability: Workflows | `/workflows/activity-availability`; activity-design settings | Effective availability/diagnostics and supported settings changes |
| ETL definitions: ETL | `/etl/definitions`; `elsa.api.etl-design` | Exact selected definition/draft, filters, validation and supported authoring |
| ETL connectors: ETL | `/etl/connectors`; `elsa.api.etl-design-v2` | Exact connector definition, authorized bindings/metadata and validated proposals |
| ETL runs: ETL | `/etl/runs`; `elsa.api.etl-runtime` | Exact run/partition and bounded evidence; supported explicit cancellation |
| Connections: Connections | `/security/connections`; connections/descriptors/picker/test/revoke/delete APIs | Metadata, bindings and diagnostics; supported changes/test actions without credentials in context |
| Secrets: Secrets | `/security/secrets`, legacy `/secrets`; metadata/descriptors/picker APIs | Reference/metadata/availability inspection; domain-authorized effects, with no secret values in context |
| Diagnostics: host and diagnostic modules | `/diagnostics`, `/diagnostics/modules`; manifest/registry/widgets | Accepted/rejected/disabled module state and bounded visible diagnostic results |
| Console: ConsoleStream | `/diagnostics/console`; selected Server/Studio console source | Current source/filter/record/truncation/freshness and authorized bounded inspection |
| Structured logs: StructuredLogs | `/diagnostics/structured-logs`; sources/recent/stream | Current filters/time range/selected records; bounded redacted correlation |
| Traces: OpenTelemetry | `/diagnostics/open-telemetry`; storage/resources/search/trace detail | Exact trace/span/resource/query with authorized detail and availability |
| Appearance/themes: host and Themes | `/settings/appearance`, `/theme-builder`; preferences/theme-store APIs | Supported preference/theme edits, exact persistence semantics and built-in/read-only constraints |
| Modules/feeds: Hosting.Nuplane | `/modules`, `/package-feeds` and settings aliases; module-management/bridge | Explain exact host/package/feed/reconcile state; package promotion/activation remains outside authorization |
| Features: FeatureManagement | `/features`, `/settings/feature-management`; modularity features/apply | Selected host, revision, filters, settings draft and reviewed differences through existing authorization |
| Optional sample: Weather | Source-configured `/weather`, absent from live loaded list | Qualify only when installed; no current composed-support claim |
| Optional ExtensionBuilder | `/extension-builder`, currently disabled | Optional inspection/guidance; no source-control push, package promotion or publisher activation |

Route and API ownership are visible in the [Workflows module](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx),
[workflow capability client](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/capabilities.ts),
[ETL client](../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/etlApi.ts),
[Connections client](../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/connectionsApi.ts),
[Secrets client](../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/secretsApi.ts),
[host](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/App.tsx) and
[contribution-derived settings](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/settingsSections.ts).

## Current request-to-effect trace

1. The host probes `/_elsa/agent/bootstrap`; the dock repeats it. Backend bootstrap uses the single
   provider's diagnostics and `AgentPolicy.Default`. The observed missing-auth result disables the
   composer. Capability listing itself does not establish permission to invoke a capability.
2. The shell mounts Weaver with only `{route:path}`. Its built-in context/capability/prompt registration
   is workflow-specific. Readiness requires an active capability, so non-workflow routes have no
   built-in live capability path even if the provider becomes available.
3. Workflow integrations contribute `api.ai` contexts, tools and proposal renderers. The running
   collector/review UI consume separate `api.agent` registries; providers receive server
   `IAgentToolRegistry` tools. The executable-detail client executor is registered but not bridged into
   that server invocation path. The shell drops explicit dispatched attachments and source metadata.
4. The workflow snapshot supplies unsaved draft/graph, selected/inspected design nodes and validation.
   It does not carry exact runtime occurrence/incident/captured-evidence identity. Lists have no editor
   snapshot. One rejecting collector causes `Promise.all` to reject the complete collection.
5. Session initialization treats any resource ID as a workflow ID. Otherwise it takes the segment
   immediately after `workflows`: `/workflows/definitions/<id>` becomes `definitions`, with analogous
   `executables`/`instances` errors. This is source-supported, not a live session reproduction under
   the unavailable provider. Initial stored context and per-message inline context are different paths.
6. Backend collection chooses only the first matching context provider; the default policy hardcodes
   four workflow context kinds. Context admission and capability/tool permission/surface selection
   need one complete generic path rather than client declarations acting as authority.
7. Both provider paths see all registered server tools. Copilot delegates the whole turn to its SDK;
   Anthropic uses the host step loop. Tool dispatch checks registration and mutation approval, but
   descriptor permissions/scopes are not enforced there. Copilot still references the Workflows
   implementation and contains workflow-specific instructions/decoding; its native harness mapping
   does not use the structured-result decoder used by its fallback step path.
8. Under review policy, generic mutation proposals are created before domain argument validation.
   Approval execution uses the registered success-reporting no-op executor. Under FullAuto the
   workflow tool creates another domain proposal, and the production deny-all workflow evaluator
   rejects it. The catalog is a hardcoded six-item list and revision reads return `unknown`.
9. Proposal status becomes Executed before the executor returns. Elsa's production message API writes
   only user messages; assistant/tool history in the host-driven loop remains local to its stream.
   Copilot delegates conversation retention to its resumed SDK session; actual SDK retention and
   recovery remain unverified here. Elsa session, proposal, audit, feedback and paused-turn stores
   default to in-memory singletons.
10. Each stream GET starts a new turn for the latest user message. There is no session exclusivity or
    replay cursor. Unresolved paused state is discarded on the next stream preparation. Cancel checks
    access to the supplied session, then addresses a global turn ID without verifying that binding.
    Audit reads likewise need an actor/tenant isolation probe. These are source-supported failure
    candidates, not demonstrated cross-tenant exploits or duplicated physical effects.

Relevant implementation: [Studio registry](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/registry.ts),
[context collection](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentContext.ts),
[dock session](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/useWeaverSession.ts),
[workflow integration](../../Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src/module.tsx),
[Agent API](../../Extensions/Agent/src/Api/AgentApi.cs),
[services](../../Extensions/Agent/src/Core/Services/DefaultAgentServices.cs),
[turn lifecycle](../../Extensions/Agent/src/Core/Services/AgentTurnServices.cs),
[registrations](../../Extensions/Agent/src/Core/Extensions/AgentServiceCollectionExtensions.cs) and
[workflow defaults](../../Extensions/Agent/src/Workflows/Services/WorkflowAgentServices.cs).

## Validation and limits

- Current-source Workbench build passes: 0 errors, 263 warnings. No product source was changed.
- The complete Agent suite passes: 136 executed, 136 discovered, 0 failed, 0 skipped. Its provider
  doubles and protocol coverage do not prove live model/tool effects.
- Five direct current-TypeScript probes confirm the registry split, missing non-workflow capabilities,
  generic collector extension seam, missing workflow-list context without a snapshot and whole-context
  failure when one contributor throws. These probes are bounded baseline evidence.
- Normal-host browser evidence verifies authenticated missing-auth diagnostics, the unavailable dock,
  and actual module load state. No model turn, successful tool execution, workflow mutation, provider
  entitlement, restart or packaged/custom-host acceptance is claimed.
- No developer-owned host or database was stopped, restarted or reconfigured. The fresh source build
  was made in this worktree and does not change the running developer binaries.

## Inputs to M1 and later qualification

### Source inventory refresh, 30 October 2026

Main `931123c7e` adds reusable workflow templates under the Workflows module: `/workflows/templates`, its selected template editor, and `elsa.api.workflow-templates` links for native list/read/create/replace/delete. Appearance now separates workspace style from color scheme. These are source-confirmed additions merged into the implementation branch; they were not part of the 29 October live observation above. [#3442](https://github.com/elsa-workflows/elsa-foundation/issues/3442) must include exact template identity/revision, unsaved editor state and explicit native persistence in its workflow coverage. M4 must use the updated appearance context. Neither source presence nor existing prompt dispatch establishes an accepted Weaver journey for these additions.

M1 should retain one active provider and the existing shared tool currency, reconcile contribution
discovery, replace workflow route inference with module-owned target/context selection, and define
typed outcomes plus server/client execution correlation. Follow the existing named contribution-event
and Slots/Contributions model. Prove a workflow candidate and a separately packaged non-workflow
candidate before freezing signatures. [#3437](https://github.com/elsa-workflows/elsa-foundation/issues/3437)
and [#3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438) own that work.

The acceptance probes should follow actual data/effects: list/detail/non-workflow resource identity;
two same-scope contributors and a new context kind; denied/out-of-scope tool never reaches its executor;
malformed mutation rejected before review; exact approved effect and receipt; duplicate streams and
cross-session cancellation; unresolved approval and second-turn/restart history; disappearance of
unsaved local targets. The owning M2 leaves preserve these obligations. M3/M4 use the matrix above;
M5 closes the full normal-host, independent-package, provider and recovery evidence.
