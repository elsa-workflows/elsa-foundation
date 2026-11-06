# Owned Jobs composed UX fixture

Status: source preparation only; no build, discovery, host, browser journey or visual acceptance has
run for this checkpoint. The [Jobs UX record](../../../specs/1218-standalone-jobs/ux-acceptance.md)
defines the operator goal, shared references and six mandatory journeys.

The existing runner selects this finite inventory with:

```bash
python3 tools/ux-acceptance/run.py --feature jobs --output /tmp/owned-jobs-ux-unique
```

It builds the current clean committed Workbench with `JobsUxAcceptance=true`, builds the same Studio
source, copies both into owned content roots and enables the fixture, actual Jobs EF SQLite/API,
native design/runtime adapters and genuine BPMN publication/runtime features only in private shell
copies. Standard Workbench builds have no reference to this project. No probe handler or control route
ships through the standard build.

The fixture registers an actual report handler which totals invoice amounts and retains useful lines.
It uses the real Jobs worker, command/query provider and API. A second registered logical handler
proves type isolation within a shared queue. Finite adverse inputs exercise actual failure/backoff,
cooperative cancellation, a handler which ignores cancellation and a cancellation-aware report barrier.
The barrier/recovery endpoint changes only this owned handler's test source; it never edits Jobs or
model state. Its control routes require the actual normalized first-party administrator and a fresh
current permission decision.

Five finite users are created through the actual first-party UserManager, IAM stores and active tenant
memberships: reader, submitter, auditor, denied and a second tenant operator. Their real permissions
remain the Jobs API security boundary. A finite administrator control revokes the actual reader's
persisted IAM permission; the original cookie must then fail direct reads and clear the old detail.
Worker scopes are host-owned `default/default` and
`default/ux-jobs-secondary`, matching those actual identities. Native/BPMN delivery exercises only the
primary tenant. No operational scope, actor, model association or identity is supplied by a producer.

The primary native and genuine BPMN cases author immutable versions, publish through their public
APIs, start normal executions and inspect retained owner/wait/result evidence. They pause both real
workflow scopes after a report starts, save the result during pause, release one reason while another
remains, resume and follow the exact parent without rerunning. The model receipt keeps failed/not-run
states and cleanup failures visible; the runner refuses those states.

The runner preserves `HOME`. Root Data Protection uses its existing public SQLite persistence options
with a private key database and per-run application name. Jobs, IAM, workflow and key stores survive
an owned backend child restart and are removed with the content roots. No keys are included in shared
artifacts. A loopback controller retains only owned child handles/groups; its random per-run token
stays in environment/memory. Node fetch, outside Playwright tracing, invokes that controller without
putting its token into browser state, arguments, logs or attachments. Startup, shutdown and controller
failures remain failures.

Jobs selection requires all six journeys in each of the four wide/narrow and light/dark projects;
narrow width is 390px. Discovery, exact source, screenshots, accessibility snapshots, traces, console
and network records are required. A real successful GET is delayed only for an explicitly labelled
loading capture. One actual committed response is discarded to test replay; real backend shutdown
exercises unavailable/recovery. No provider/API response body or model authority is mocked. An
unexpected browser/server/network failure, skipped or retried execution, missing artifact, incomplete
model receipt, dirty source or changed revision refuses a passing machine receipt.

The 13 isolated runner controls pass with fake processes/sockets and private files. The lifecycle
barrier covers a published child in startup, concurrent teardown and a queued late control; it does
not execute an SDK or real host. Root integration must generate the fixture's locked restore inputs
and run the actual composed package/browser checks. Downloadable exact-run artifacts
and independent opened visual/interaction review are still required; source preparation does not
establish product acceptance or the wider provider/model qualification matrix.
