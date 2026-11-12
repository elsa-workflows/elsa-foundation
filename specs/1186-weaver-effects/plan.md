# Implementation Plan: Weaver effects and effective session autonomy

**Branch**: `agent/codex/3440-weaver-effects` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Status**: Approved for task generation after independent correctness and acceptance/standards planning reviews on 2026-11-01. No shared contract is frozen.

**Refreshed implementation base**: Fast-forwarded to main `94be29ece1b3fd55b534a6a9043d3b2b94f5e33c` before implementation. Incoming Sketchbook appearance changes leave Agent, Weaver transport/UI, qualifier fixtures and constitutions unchanged; they remain in normal/package composition. Main checks are still running with no current failed checks or open `main is red: CI` title. Earlier `c0b47ceb1` proof remains source-bound historical evidence, not fresh qualification of this base.

## Summary

Reuse the existing module preparation/proposal/executor/receipt path. Demonstrate a legitimate automatic personal Home card size change through the separately packaged Acme module, keeping visibility and workflow edits review-required. Reproduce the established-conversation dropdown/server policy mismatch before correcting it. Extend the existing session owner with the smallest coherent admission and acknowledged autonomy operation, preserving unrelated policy and accepted-work attribution. Complete correlated expiry and owner-bounded audit queries.

The causal package journey selects the public contract. The reviewed plan permits experimental changes within this leaf; a candidate API is not the final extension contract until both actual provider adapters and package consumers demonstrate its behavior. Do not add a second execution, contribution, admission or review engine.

## Technical Context

**Language/Version**: C#/.NET 10; TypeScript/React; Python 3 conformance driver.

**Primary Dependencies**: Existing CShells, named Events collection lifecycle, Agent Core and actual Copilot/Anthropic adapters; Studio public SDK; native Studio Preferences service/store.

**Storage**: Existing process-local session/proposal/paused-turn/audit state; real native personal Home preference CAS in the independent fixture. Durable/distributed recovery is #3441.

**Testing**: xUnit behavioral/API tests with controlled barriers and fake clocks; Vitest HTTP transport/hook behavior; Playwright/native composed journeys; external package authors and consumers.

**Cadence**: The constitutions prescribe risk-based behavior tests, without a global greenfield TDD cadence. Under the owner's delegated routine technical authority, use causal baseline/red-green controls for changed behavior and tests alongside each increment; no extra owner decision is needed. Passing isolated tests does not replace native or integration gates.

**Target Platform**: Supported .NET backend and Studio browser, local macOS and hosted Linux package consumers. No remote publish/deployment or live-provider credential change.

**Performance Goals**: Short atomic state transitions; no monitor held over module/provider/network I/O or per-proposal/call waits. Bounded owner audit query; no unbounded global read plus N session reads.

**Constraints**: Exact prepared binding/current authority; one accepted foreground work item; truthful uncertainty/no replay; no policy change overtaking accepted work; no silent conversation rotation.

**Scale/Scope**: #3440's five acceptance criteria plus admission, expiry and scoped-audit follow-through. Reuse #3439's verified context/disclosure/receipt controls. Later workflow/module coverage remains #3442/#3443; no existing criterion is moved to those leaves.

## Constitution Check

Both ratified constitutions are loaded: framework v6.1.0 and Elsa v6.3.0, plus the contributor review checklist. Initial review passes with these constraints; repeat after causal contract selection.

| Gate | Application |
| --- | --- |
| Framework §2.1/2.5/2.6/2.18 | Public replacement contracts/models stay in Agent Core, mechanisms in Agent; no unrelated implementation reference or module knowledge in generic Weaver. Preserve existing named contribution fan-in. |
| §2.5.1 | Session/lease/pause/proposal/audit state remains process-lifetime data; scoped execution resolves current tools/authority. No singleton captures request/module callbacks or HttpContext. |
| §2.6.2/2.11 | Extend the existing replacement owner. Diagnose duplicate owners; unsupported legacy replacement support refuses explicitly rather than silently bypassing atomicity or ownership. |
| §2.10/2.23.2 | Owner-bounded audit query is distinct from emission. Behavioral tests cover policy, concurrency, uncertainty and query limits; no assertions on documentation text. |
| §2.21/2.23.3/2.23.5 | Preserve valid existing behavior/tests. Internal-by-default implementation; additive public surface tracked. Infrastructure failures receive domain-boundary wrapping and truthful operation outcomes. |
| §2.22 | Update Agent catalog and Acme cross-domain contribution documentation with the actual delivered surface in this PR. |
| §4/Elsa §E5/E7 | Classify additive contract changes against baseline; computed package versions/ranges remain normal. No new test project is planned; retain existing tier membership. |
| Elsa §E2.2/2.6/2.8 | No runtime/design coupling, executable or picker changes. |

Framework §2.24 is draft; Elsa activity-versioning/Model X/definition-state sections are provisional. This work changes none of those provisions and does not claim their ratification.

## Project Structure

```text
specs/1186-weaver-effects/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/session-policy.md
Extensions/Agent/src/
  Core/Contracts/, Core/Models/, Core/PublicAPI.Unshipped.txt
  Elsa.Agent/Services/, Elsa.Agent/Extensions/, Elsa.Agent/EXTENSION_POINTS.md
  Api/Models/, Api/AgentApi.cs
Extensions/Agent/tests/Tests/
Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/
  agent/agentClient.ts, agent/agentTypes.ts, agent/__tests__/
  weaver/useWeaverSession.ts, weaver/WeaverSurface.tsx, weaver/__tests__/
tools/studio/Weaver.Conformance.Probe/
  fixtures/Resource.Backend/, fixtures/Resource.Backend.Tests/
  fixtures/Resource.Studio/, journeys.py, agent_transport.py, browser.mjs
```

**Structure Decision**: Extend existing owners/projects and qualification fixture. No new production module, generic resource schema, provider implementation or host-specific domain hook. Exact paths are verified before edits; the tree describes ownership, not a promise of new files.

## Ordered Delivery

1. Establish the legitimate automatic fixture and causal baseline on unchanged production behavior. Retain old review-required controls and independent native owner before/after observations. Record the same-session mode mismatch, not an unproved duplicate-pending bug.
2. Implement one session-owned ordering of policy changes and message collection/admission, foreground leases, pause/review, direct invocation/preparation, dispatch, pending acknowledgement and receipt continuation. Do not release ownership merely because a message is terminal. Preserve duplicate identities and the existing message insertion lock; make losing outcomes explicit.
3. Add authenticated autonomy-only transport and authoritative policy readback, then Studio acknowledged/pending/uncertain-setting UI. Preserve conversation, history, accepted receipts, unrelated restrictions and policy attribution of accepted turns/results.
4. Add correlated pre-dispatch expiry and distinct post-dispatch uncertainty records. Move authenticated actor/tenant filtering/order/limit into the owning audit reader, with legacy support failing closed.
5. Complete causal regression/barrier tests, the thirteen explicitly named native autonomy/lifecycle cases per adapter in [the contract inventory](contracts/session-policy.md#mandatory-native-qualification-inventory), original38 plus four current-authority package cases, affected complete suites and normal composed Studio verification. Freeze the minimum contract only after normal composed and complete package proof, independently review the complete diff, and pass exact-head checks before normal merge and resulting-main qualification.

All experiments are source/input-bound evidence. Partial or expected-failing baselines cannot mark this feature qualified. No acceptance item or Project state becomes Done until final integration and resulting-main records agree with actual evidence.

## Complexity Tracking

No constitutional exception is planned. Admission ownership belongs to the existing session boundary; the foreground cancellation registry and paused transcript store retain their distinct jobs. Reject a design that merely adds another busy flag beside unsynchronized state.
