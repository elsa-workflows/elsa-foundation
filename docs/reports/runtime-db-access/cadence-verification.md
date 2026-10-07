# Effective checkpoint cadence: T09 verification

**Outcome:** the existing cadence choices work through the normal Workbench authoring, publishing, HTTP-start and persisted-inspection path. The fixture now accepts authored cadence and segment-cap inputs; no runtime behavior or default changed. This delivers the bounded verification for [#2399](https://github.com/elsa-workflows/elsa-foundation/issues/2399), subject to PR and resulting-main gates.

## Measured fixture results

On 7 October 2026, a clean source build at `81b7e94dee8dac452ac94e922c71b9496e324c14` ran against a fresh owned PostgreSQL 16.15 runtime database and separate isolated SQLite diagnostics. The primary workflow used HttpEndpoint, the deterministic SetVariable intrinsic, and WriteHttpResponse. All six requests returned HTTP 200 with exactly `Alice Smith`, reached `Completed`, and exposed a present `incidentCount` of zero.

| Host mode / cap | Published authored choice | Effective run cadence / cap | Inspection |
|---|---|---|---|
| Coalesced / 50 | Absent | Coalesced / 50 | boundary-level |
| Coalesced / 50 | Immediate | Immediate / null | activity-level |
| Coalesced / 50 | Coalesced / 8 | Coalesced / 8 | boundary-level |
| Coalesced / 12 | Coalesced, cap absent | Coalesced / 12 | boundary-level |
| Immediate / 12 | Coalesced / 8 | Immediate / null | activity-level |
| Immediate / 12 | Absent | Immediate / null | activity-level |

The same database remained in place through two real host-process restarts: Coalesced/50 to Coalesced/12, then Immediate. Seven authenticated rereads of the original execution IDs retained their original cadence, cap and inspection granularity. The new executions used the reconfigured host settings as shown above. Each case retains the exact publication's definition, version, artifact and source-reference IDs in the [machine-readable results](evidence/t09-cadence-verification.json).

## Mandatory boundaries

A separate Coalesced/50 workflow set a variable, suspended on an Event bookmark and left its output step unexecuted. After the first restart its full instance-detail bytes were identical, including the original Coalesced/50 stamp and exactly one Set and one Wait activity record. One ResumeOnly stimulus matched one execution; it completed with the original saved value, one Echo record, one Set record, and zero incidents.

A separate poisoned WriteLine input produced one blocking `WaitForIntervention` incident while its workflow remained `Running`. After that same restart, both the instance-detail and incident-list bytes were identical, including the original cadence stamp and incident resolution. This checks a nonterminal incident's persistence independently of the completed HTTP cases.

These are normal-host persistence observations. The 36 focused existing tests additionally passed with zero failures/skips: cadence resolution, mandatory-boundary policy, authored-cadence bookmark flushing, terminal equivalence and coalesced cancellation. The mandatory-boundary theory covers completion, fault, cancellation, incident and bookmark cases. These tests and the guide distinguish the segment cap's checkpoint records from activity count. A lower cap is not a request to execute fewer activities.

## Reproduction and operator guidance

Use the [cadence guide](../../runtime-durable-resumption.md) and the existing [HTTP reference fixture](../../../e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1) against an owned disposable host. Configure the host separately, pass `-AuthoredCadence`, optional `-AuthoredMaxSegmentCheckpoints`, and the expected effective cadence/cap. The default fixture call still authors no override and expects Coalesced/50. For a non-50 host-cap fallback, explicitly pass `-ExpectedMaxSegmentCheckpoints`.

The guide describes publishing a new workflow version after an authored change, reading the same persisted execution after host reconfiguration, SQL command logging for diagnostics, and the bounded replay window. The separate [Studio badge investigation #1242](https://github.com/elsa-workflows/elsa-foundation/issues/1242) and [inspection decoupling #1237](https://github.com/elsa-workflows/elsa-foundation/issues/1237) retain their scope; neither was implemented here.

## Evidence and limits

The Workbench project build passed with 101 warnings and zero errors. Ten offline parameter/validation controls and PowerShell parsing passed before the live run. Root reviewed the fixture and private protocol; independent review checked the boundary assertions before execution and accepted the retained raw results and public projection afterward. The public JSON pins the retained scripts, metadata, raw boundary files and private evidence archive (`e8a7851737940ca778991b94ca2c35e8e8f461af87e68fe63d2a42e2a8dd51e2`). The archive is private and contains configuration snapshots, full case/readback payloads, owned host logs and retained diagnostics SQLite data. Raw diagnostics are not part of the public JSON. The owned host root and PostgreSQL container were removed, and the generated `pg.env` and `secrets.json` credential files were deleted. Existing databases were not used.

This is six sequential HTTP controls plus bounded bookmark/incident controls, not concurrency or performance evidence. No SQL count, query reduction or general latency improvement was measured. Exactly-once external side effects are not promised. Final integrated fault, malformed-input, concurrency, recovery and before/after acceptance remain with T17/T18. The historical failed Coalesced C4 control and its unresolved attribution remain unchanged.
