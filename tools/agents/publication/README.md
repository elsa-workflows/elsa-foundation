# Specialist publication and lifecycle research

Historical M0 witness: its expected BPMN dependency-gap assertion is source-bound. For current M4
package-boundary qualification use the [new conformance profile](../conformance/README.md#bpmn-package-boundary-profile).
The original research fixture intentionally refuses an unexpected closure change and is not the current
product acceptance runner.

Bounded M0 witness for [#3533](https://github.com/elsa-workflows/elsa-foundation/issues/3533),
within [Elsa Agents #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
These external fixture packages are experiments, not shipped extension APIs.

Run with .NET 10 and Python 3.12 or later, from the repository, using a new absolute output
directory outside the checkout and a unique local package version:

```sh
python3 tools/agents/publication/qualify.py \
  --output /absolute/new-specialist-proof \
  --version 4.0.0-specialist3533.local1
```

The driver snapshots committed product source, builds and packs its 38-project dependency
closure, then builds the independent Runtime and Design fixture packages against that local
feed. Author, Consumer and Lifecycle are separate package-only executable projects. Their
locks and package caches live outside the repository. No provider request, existing database,
user configuration, remote package publication or frontend build is used.

Author invokes the actual `ActivityDefinitionPublisher`, provider-neutral validator/compiler,
and ordered EF publication command against three disposable SQLite databases. Missing consumer
and storage capabilities must refuse publication before any template/publication is written.
The positive path publishes `copywriter-v1`, checks publication idempotency, and compiles a
changed candidate without rewriting the earlier artifact. Only the exported template enters
the runtime inbox.

The native Consumer has no Design or Publishing package dependency. It invokes Elsa's actual actor,
scheduler, activity hydration and checkpoint path with the separately packed specialist runtime
strategy and Microsoft Agent Framework 1.27.0. Native and BPMN executable fixtures use the same
published child. Each verifies exact input, model, instructions, schema, committed JSON result
and duplicate-start behavior. The invalid-output control must fault without an accepted result.
These are authored executable fixtures; Studio authoring and BPMN XML import are not covered.
The separate BpmnConsumer records an existing coupling: `Elsa.Activities.Bpmn` brings in
`Elsa.Workflows.Design.Core` and `Elsa.Activities.Design.Core`. It makes no mutable draft lookup,
but its package closure is not Design-free. The qualifier preserves this as `ObservedUnresolved`
for M1/M4 and rejects an unexpected change to that closure. It does not weaken the native/runtime
package guard to hide the finding.

Lifecycle probes a disposable SQLite CAS sketch around real MAF session serialization. It tests
tenant/version/conversation keys, competing turns, atomic accepted session/result replacement,
duplicate/late completion, cancellation fencing and an explicitly recorded unknown outcome.
The caller supplies the scope; this is no IAM proof. The unknown outcome is injected as a
lifecycle event, not produced by a transport fault. This sketch does not implement Elsa's
production recovery journal, a process-death witness, lease recovery or conversation reopening.

`report.json` retains every stage's exit code, product package hashes, fixture hashes and
resolved package identities. Negative children retain `Failed` with the exact expected marker;
a wrong-cause failure fails the driver. Candidate archive bytes are compared with the isolated
NuGet cache. A fresh run must finish all stages; partial output is not a qualification receipt.
The final status is `PassedWithRecordedGap`, rather than full product qualification.
The detailed [research report](../../../docs/reports/elsa-agents-publication-research.md) owns
recommendations and limits. The earlier [SDK witness](../research/README.md) owns streaming,
tool/approval semantics, SDK cancellation and separate-process session restoration.
