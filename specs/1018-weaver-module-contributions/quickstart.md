# Candidate Validation Guide

This guide defines implementation acceptance runs; it does not claim they have passed. Complete artifact review and task analysis before implementation. Use the existing [owned-host process](../../e2e-tests/README.md), current repository SDK/Node/pnpm and build-slot wrapper. Check `uptime` before diagnosing timing failures. Use fresh owned host ports/state; never stop or reconfigure a developer host.

## Composition and targeted checks

Explicitly discover ordinary Events infrastructure with the Agent leaf/default composition. Assert the stable configured feature name, leaf Type dependency, idempotent repeated registration and clear failure without Events. Deterministic providers belong to test fixtures; live-provider testing stays opt-in. Current Copilot missing-auth diagnosis is not fixed by fabricating credentials.

After implementing the candidate, run affected project checks, including:

```bash
dotnet test Extensions/Agent/tests/Tests/Elsa.Agent.Tests.csproj
pnpm --dir Studio --filter @elsa-workflows/studio-web test
pnpm --dir Studio --filter @elsa-workflows/studio-workflows test
pnpm --dir Studio --filter @elsa-workflows/studio-weaver-workflows test
pnpm --dir Studio --filter @elsa-workflows/studio-sdk build
pnpm --dir Studio --filter @elsa-workflows/studio-workflows typecheck
pnpm --dir Studio exec playwright test --config tests/browser/playwright.weaver-composed.config.ts
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check
dotnet restore Elsa.Server.slnx --locked-mode
git diff --check
```

The browser configuration requires `ELSA_WEAVER_OWNED_URL`, `ELSA_WEAVER_WORKFLOW_ID` and `ELSA_WEAVER_ACTIVITY_ID` for an explicitly owned Studio and seeded workflow child. Set `ELSA_WEAVER_OWNED_BACKEND_URL` when the backend has a separate origin, `ELSA_WEAVER_OWNED_AUTH_STATE` to the private state from a fresh normal login, and `ELSA_WEAVER_OWNED_ARTIFACTS` to the owned evidence directory. It starts no hosts and rejects the developer host ports. Preserve decoded receipts and sanitized logs as review evidence; authentication state and raw traces remain private.

Run affected build/lint/package checks required by changed projects and compare project/build evidence after the Core split; no blanket solution test run is implied. Source composition is candidate evidence. The independent package witness below remains a separate gate.

Stop detaches the foreground conversation immediately; the next prompt creates a fresh server session. Earlier changes, receipts and rendered results remain available. A late acknowledgement still confirms its original invocation/session, without replaying its mutation or reopening the stopped conversation. When the turn ID has not arrived, only that detached stream may learn and cancel its original turn; it cannot overwrite a new session. Durable paused-turn cancellation/history and recovery remain M2 qualification work.

## Two positive journeys

1. Open a workflow draft with an existing node in the owned composed host. Invoke the module prompt action with explicit attachment/source/correlation. Observe exact definition ID, draft revision, selected node and admitted attachment in context, with redacted/excluded content diagnosed.
2. Prepare a harmless `SetDesignerPosition`. Inspect target/revision/arguments/risk in module-owned review content. Even in FullAuto, local execution requires explicit review. Apply receives pending HTTP 202; only the originating live binding may execute.
3. Observe actual editor document position and new revision before acknowledgement. Verify Executed receipt and resumed turn use that result, native validation ran, dirty state changed, and nothing was implicitly saved/published. Whole-batch undo restores the prior state and refuses after an intervening revision.
4. In an unrelated fixture module, inspect its server resource, prepare/review a harmless value edit and apply through its owning service. Read the resource back and compare value/revision with the typed receipt. Shared host/provider code gains no resource branch.
5. Exercise both existing provider paths using deterministic protocol fixtures; compare admitted tool and typed result meaning. Label this evidence separately from deterministic orchestration tests and any opt-in live-provider smoke run.

## Negative and incomplete paths

| Situation | Observable expectation |
|---|---|
| Denied actor/tenant, malformed arguments, stale revision, missing executor | Explicit refusal, zero effects before dispatch/application |
| Missing required context / optional source unavailable | Dependent action denied / visible partial context; unexpected exceptions and cancellation propagate |
| Navigation, editor unmount, module removal or replaced selection before dispatch | Generation mismatch refuses; no replacement target or `active-draft` fallback |
| Transaction committed then acknowledgement response lost | Retain and retry the exact acknowledgement without rerunning the module effect, then drain the stored outcome; repeated transport failure exposes confirmation recovery and does not silently reopen a busy session |
| Duplicate request/receipt, conflicting payload or late acknowledgement | Identical delivery coalesces; conflict diagnosed; accepted effect cannot execute again or reopen |
| Changed permissions between review/application | Current backend policy denies before effects |
| Older in-flight/queued/stale save completes after a reviewed edit | New hold and whole-change undo survive; no background write of the reviewed state; only a successful covering explicit save releases the hold |
| Covering save fails during Publish | Real publication caller stops before preflight/promotion/publication, preserving the held draft and undo |
| Renderer predicate or component throws | Owning structured preview/receipt and shared review controls remain usable |
| Authoritative terminal approval/denial/execute refusal, including expiry before approval | Terminal proposal/error is shown; original caller settles once; retained outcome drains with no prompt/tool replay and the next prompt is accepted; nonterminal revision conflicts remain reviewable |
| Resource identity/revision/provenance supplied only in the admitted envelope | Both provider protocols preserve generic target/module/schema/origin metadata without interpreting domain content or exposing excluded sensitive content |
| Duplicate identity, unsupported version, absent renderer/counterpart | Explicit admission/availability diagnostic and safe rendering or refused execution |
| Instruction-shaped resource data or secret content | Data cannot grant authority; permitted/redacted/excluded context is explicit |

Record exact commits, owning resource read-back, correlated request/receipt IDs and relevant screenshots/logs without secrets. Distinguish a dispatched/pending request, local working-state application and durable server effect.

## Independent package witness: #3438

Pack actual .NET packages and frontend distribution artifacts into isolated feeds/output, then restore/build an independently authored non-workflow module and consumer host using those artifacts. No project references, copied shared implementation or Weaver/host/provider edits may make the witness pass. Prove context, tool, review and observed resource effect, then module removal and unsupported version behavior. Record the artifact versions/dependency closure, host command and owning resource evidence; amend candidate signatures where needed before freeze.

Use one implementation leaf and one integration/review lane. Record transitions and evidence on the owning issue/PR and Program board; request authorized repository review, verify exact reviewed head/checks, then resulting main. Keep M2 recovery and M3/M4 full current-module journeys open until their separate acceptance is verified.
