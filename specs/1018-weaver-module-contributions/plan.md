# Implementation Plan: Module-owned Weaver contributions

**Branch**: `agent/codex/3437-weaver-contributions` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

## Summary

Deliver #3437 as a connected candidate contract: evolve existing Agent sources and Studio contributions, preserve complete prompt requests, move domain behavior to its owning modules, and prove one native workflow working-state change plus one unrelated server effect. #3438 then consumes real packages in an independent module and gates contract freeze. The [M0 baseline](../../docs/reports/weaver-baseline.md) remains the inventory; M2 durable recovery and M3/M4 broad journeys remain separate leaves.

The native example uses a harmless `SetDesignerPosition` on an existing node, exact draft identity/revision and observed editor state. It preserves native atomic validation, dirty state, guarded whole-batch undo and explicit save/publish. Its bounded client bridge is delivered here, not deferred.

Source refinement: compare the expected revision at the native current-state/reducer transaction boundary, so a queued user edit cannot race a stale hook snapshot. Existing autosave defaults to enabled; a module-owned working-state hold prevents a reviewed Weaver edit from entering that debounce until explicit save/publish or its valid whole-batch undo. Preserve the stored autosave preference and show the unsaved state through normal module UI.

## Technical Context

| Item | Decision |
|---|---|
| Language/platform | .NET 10/C#, current repository SDK; TypeScript/React 19 and existing CShells composition |
| Dependencies | Existing Events.Core, Agent contracts, Microsoft.Extensions.AI and current provider SDK adapters |
| State | In-memory session/proposal/client-invocation state for this candidate; no durability claim |
| Tests | Existing Agent xUnit, Studio Vitest and browser infrastructure; owned composed host, architecture/composition and package-consumer qualification |
| Scope | #3437 candidate and two thin journeys; #3438 independently packed module proof before freeze |
| Constraints | No universal resource model, second tool engine, new credential/provider default, implicit save/publish, release or deployment |
| Performance | No new numerical target; cancellation and bounded invocation expiry are observable behavior |

## Constitution Check

Framework v6.1.0 and Elsa v6.3.0, plus the [review checklist](../../docs/contributing/review-checklist.md), govern this design. Draft §2.24 and provisional Elsa material are not ratified by this plan.

| Gate | Design and required evidence |
|---|---|
| §2.1, §2.17 Core shape | Move active defaults/registration into new `Elsa.Agent`; Core retains contracts, models, named events and genuinely thin bridges; remove its feature registration/manifest generator if no longer used. Remove all 16 Agent implementation-shape exceptions after moving their types. |
| §2.5, §2.19 composition | Preserve configured `FoundationAgentAbstractions` identity; default feature has virtual/idempotent registration. API/provider/workflow features inherit it and declare Type dependencies. Ordinary Events infrastructure is explicitly discovered. |
| §2.6 contribution fan-in | Named Sequential collecting events, one aggregator per event and existing contributor interfaces. Expected optional absence is typed; unexpected exceptions/cancellation propagate unshielded. |
| §2.7, §2.14 boundaries | Provider adapters transport typed results; workflow modules own workflow context, renderers, revision checks and native edits. No host domain branch. |
| §2.21, §2.23 tests/API | Preserve valid tests; test policy/effects/composition, not document text. Implementations internal by default; public consumer extension points and API/SemVer delta reviewed explicitly. |
| §2.22, Elsa §E7 | Update affected feature catalogs and EXTENSION_POINTS in the implementation unit; architecture/map/test-tier checks cover changed projects. |
| Elsa §E2.2.3, §E2.6 | No new Workflows.Runtime → Design dependency. Authoring integration consumes the owning design contracts. Qualify supported server and package host shapes. |

Pre-design result: no requested exception. Post-design result: the named fan-in, default split and bounded acknowledgement bridge satisfy the gates in design; implementation evidence and independent review remain required before approval. Any gate failure blocks progression rather than gaining an allowance here.

Independent standards review found no actionable design blockers. Root reviewed the artifacts and accepted this candidate for task generation under the owner's existing delivery authorization; this is not final contract ratification. Existing application code is being evolved, so the greenfield application cadence choice is not introduced; use risk-based behavior tests and causal red/green probes for changed behavior.

## Project Structure

```text
specs/1018-weaver-module-contributions/
  spec.md  plan.md  research.md  data-model.md  quickstart.md
  contracts/contributions.md
Extensions/Agent/src/
  Core/                          # contracts/events/thin bridges
  Agent/Elsa.Agent.csproj         # new actual defaults and stable feature
  Api/                           # generic API and acknowledgement transport
  Workflows/                     # workflow backend contribution
  GitHubCopilot/  Anthropic/      # existing generic provider adapters
Extensions/Agent/tests/Tests/     # existing tests + deterministic fixtures
Studio/src/essentials/
  Elsa.Studio.Hosting/Client/src/ # canonical SDK, generic shell/agent/Weaver
  Elsa.Studio.Workflows/Client/   # private live binding/native transaction
Extensions/Weaver/studio/
  Elsa.Studio.Weaver.Workflows/Client/ # workflow contribution/presentation
Studio/tests/browser/            # composed candidate journey fixture
```

#3438 uses an independently authored module and consumer host consuming packed artifacts, with no project references to shared implementation.

## Delivery and Review Sequence

1. Independently review these artifacts, then generate/analyze dependency-ordered tasks before implementation.
2. Split Core defaults, internalize implementation types, preserve feature identity and prove leaf/default/Events composition and duplicate registration behavior. Singleton state stores contain data only; scoped facades/executors resolve tools and current actor without retaining HttpContext/scoped services.
3. Evolve context/capability/tool sources through named collecting events and one aggregator each; connect existing registry lookup to the admitted snapshot. Add typed outcomes, duplicate/version diagnostics and generic prepared/result models.
4. Extend existing proposal execution with server effects and bounded client requests/acknowledgements. Client mutations require explicit review, including FullAuto. Apply returns pending (HTTP 202); only an authenticated observed-state receipt marks Executed and resumes the paused turn with the stored result.
5. Migrate every prompt/context producer to canonical `api.agent`, preserve attachments/source/correlation/mode, and move workflow host/provider decoding into workflow-owned contributions. Use existing Slots/Contributions, module-private live bindings and direct execution callbacks.
6. Prove the native workflow layout edit and unrelated server-resource edit through composed Studio/backend and both deterministic provider-protocol paths. Qualify pre-dispatch refusal, post-dispatch uncertainty, duplicate/conflicting delivery and normal editor behavior.
7. Review and merge #3437 through the authorized process, verify resulting main and record owning issue/board evidence. Then #3438 packs and consumes the contract independently, proves effect/removal/incompatibility and drives necessary revisions before freeze.

## Complexity Tracking

Feature dependency attribute arguments use the existing object-array syntax, such as `new object[] { "Events" }` and `new object[] { typeof(FoundationAgentAbstractionsFeature) }`.

No constitution violation is authorized. The new `Elsa.Agent` package is the actual default implementation previously misplaced in Core; the client bridge extends the existing proposal lifecycle and native editor seam rather than introducing another execution engine.
