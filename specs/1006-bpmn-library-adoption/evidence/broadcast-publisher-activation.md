# Schema-gated publisher activation

Source parent `b5b32cc2a`; the [machine receipt](broadcast-publisher-activation.json) binds the exact
source and controls. This is the bounded T050 publisher activation increment. **Actual crash/recovery
cuts, final current Studio, remaining semantic stories, the library switch and program acceptance remain
open.** The candidate already uses the released BPMN adapter; delivered-main library-switch acceptance
remains open and the legacy writer remains present.

The existing handler contribution now declares recipient protocol 1 together with its durable-source
requirement. Ordinary and scope-only constructor/registration defaults are unchanged. The normal
committer stamps only the declared protocol and authoritative physical scope; it does not select recipients.
One kind cannot have conflicting protocol registrations. Unsupported versions and source-free protocol
registrations refuse before changing composition.

The Primitives publisher asks the shared `ISchemaDormancyCheck` for outbox family 2.0.0 on the final
enriched envelope, before persistence decisions, fingerprinting or writes. Missing, unobserved, seeded,
held and not-yet-adopted support refuse. A later enricher cannot add a publish past an earlier check.
The additive default `ValidateFinalAsync` hook preserves existing enrichers. Materialized work prevents
a successful Skip; materialized and declared responsibilities are checked together before canonical
folding. Unrepresented or conflicting work refuses before writes, while equivalent dual representations
retain the ordinary one-row form. The acknowledgment guard counts the actual outbox handed to the store.

`PublishStimulusExecutor` accepts only the existing committed source context and calls the saved-cohort
router. Its original intent-only signature refuses before routing; there is no permissive fallback.
Domain/source boundary refusal is translated to the processor's existing owed-responsibility boundary,
retaining its cause and availability classification. It leaves the claim, failure history and attempt
count intact. Actual repaired retry controls deliver successfully; ordinary delivery failures and
admission shedding keep their existing retry behavior.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Whole Workflows Runtime | 3,041 | 0 | 0 |
| Whole Runtime EF | 1,649 | 0 | 0 |
| Whole Activities Runtime | 720 | 0 | 0 |
| PostgreSQL source convergence smoke | 2 | 0 | 0 |
| Architecture/API guards | 12 | 0 | 0 |

The three whole suites contain **5,410** cases, a net increase of 33: 26 new publisher-pipeline cases,
eight protocol-contribution cases and two intent-only refusal cases replace three obsolete projection
cases already covered by the owning adapter. Nine existing payload-shape/local-event cases now use the
real native committer, processor, saved router, default dispatcher and actual queued callback. Two existing
SQLite convergence cases now use real committed sources, fenced claims, persisted winners and actual
actors across fresh providers. They include recipient acceptance before source acknowledgment and a stale
owner released after peer takeover. The same contract passes PostgreSQL. Its clocks and fleet answers
are controlled; real module gates, finalization records and shared observations are used. This remains
provider component evidence, not an upgraded-host crash/recovery claim.

Eight behavioral mutants failed assertions without compilation failures and exact source was restored
before whole verification. Discovery/TRX reconciliation, source bindings, Core API **four additions/zero
removals**, default locked restore of all 339 Server projects and map checks are in the machine receipt.
The 35 lockfile updates preserve every existing external package record; required project edges and the
new test bridge's dependency closure come from the actual NuGet graph.

The [owned host observation](broadcast-publisher-activation-host.json) records a separate normal-API
candidate probe over the preserved legacy database. Its legacy backend/controller were stopped, the
listener and database handles were absent, and a coherent private snapshot was preserved before checked
inventory found ten outbox rows, one settled legacy broadcast and zero unresolved publish responsibility.
That private whole database contains identity configuration and is not published. Existing portable
legacy captures remain unchanged. This is local writer exclusion, not production-wide quiescence.

The candidate resumes that owned database using its normal schema/fleet machinery, observes outbox
family 2.0.0, and passes four registered BPMN import/publish/execute/export scenarios. One imported signal
publisher completes both already committed catches and their parallel join without incidents. Checked
post-inventory distinguishes the preserved settled legacy source from the new ProtocolDelivered source;
its actual saved cohort has protocol 1, retention witness 1 and two captured resumes. The receipt binds
candidate source/Release assemblies and explicitly records its uncommitted candidate provenance. It is
not final reviewed-head/resulting-main evidence or proof of the remaining crash cuts.

Three independent component reviews found and closed boundary-accounting, late-enrichment and
materialized-responsibility gaps. Full-PR review rounds remain **0**. Final whole-PR review, required
checks, protected merge, resulting-main verification and later ADR 0063 acceptance remain open.
The frozen authoring fingerprint is unchanged:
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
#2898/#2907 and production program acceptance remain open; Orleans/task-runner follow-up remain deferred.

Provenance clarification: earlier bounded checkpoints called the candidate adapter dormant. Source
inspection confirms `ActivitiesBpmnFeature` registers `BpmnProcessRuntime` and `BpmnProcess` consumes
that SPI, so the candidate's imported BPMN probes execute the released interpreter. The outstanding
library switch is verified delivery on main, together with the remaining compatibility/recovery gates.
