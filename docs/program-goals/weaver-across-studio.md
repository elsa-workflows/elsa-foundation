# Weaver across Studio

- **Status:** Active; full M0–M5 autonomous delivery authorized, 29 October 2026. Establishment is complete; product acceptance remains open.
- **Area:** Studio-wide assistant context, tools, review and supported effects through participating modules.
- **Steward:** Sipke Schoorstra; one implementation leaf at a time, with one integration/review lane.
- **Charter, decisions and progress:** [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428).
- **Kanban WIP:** [Project #65, view 1](https://github.com/orgs/elsa-workflows/projects/65/views/1).
- **Full hierarchy:** [Project #65, Roadmap](https://github.com/orgs/elsa-workflows/projects/65/views/2).

## Purpose and ownership

Every current Studio context works through module-owned contributions. A future Weaver-aware module
can add context, tools, guidance, review UI and supported actions without changing Weaver core, the
Studio host or provider adapters. Contexts include screens, lists and filters, resources, selections,
unsaved editors, activities, expressions, exact execution occurrences, diagnostics and module surfaces.

Use the existing [Slots and Contributions decision](../../Studio/docs/adr/0001-use-slots-and-contributions-for-studio-extensibility.md)
and [resource-agnostic Weaver decision](../../Studio/docs/adr/0003-make-weaver-tools-and-proposals-resource-agnostic.md).
The [Studio domain context](../../Studio/CONTEXT.md) and [Elsa glossary](../glossary/elsa.md) own meanings;
the Program issue owns the acceptance matrix and current evidence. Candidate API signatures remain
subject to demonstrated journeys and normal architecture review. The independent M1 minimum is accepted and verified; later lifecycle changes require their own qualification.

Weaver owns generic orchestration, discovery, transport, policy, review lifecycle, cancellation and
audit. The Studio host owns generic surface/selection lifecycle and the common review shell.
Participating modules own resource identity, minimized context and domain guidance, tools/schemas,
validation, domain risk assessment, result rendering and application semantics. Provider adapters
translate the provider-neutral protocol to their SDK, preserving the single-active-provider rule.
Administrator policy has final veto; the authenticated user remains the permission actor.

The [29 October owner continuation](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6509190533)
authorizes implementation, tests, documentation, scoped PRs, review, authorized merges and resulting-main
verification across M0–M5. It supersedes the establishment-only session boundary. Routine choices and
milestone transitions proceed autonomously; escalate consequential decisions outside scope, missing
authority or genuine external blockers while continuing independent useful work.

The [30 October scoped exception](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6521994194)
authorizes bounded diagnosis and concrete-fix verification after #3438 reached the organization’s
three-failure startup stop. It preserves the full delivery scope and normal acceptance, review and
merge gates. The issue records the fresh claim, diagnostic limits and observed results.

The [M0 baseline](../reports/weaver-baseline.md) records authenticated `missing-auth` diagnostics,
the actual loaded-module inventory, source wiring findings and the initial finite coverage matrix.
It does not establish a successful provider turn or applied effect.

The [31 October context-verification exception](https://github.com/elsa-workflows/elsa-foundation/issues/3439#issuecomment-6542261312)
authorizes bounded resumption of #3439 after its compilation stop. The original failed attempts remain
recorded; no acceptance, review, package or resulting-main gate is waived.

The [31 October review-cap amendment](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6546731187)
reaffirms full end-to-end delivery and extends the review ceiling to 20 total rounds per Program PR.
Completed rounds remain counted: #3514 resumes at round 7. Independent review, exact-head checks,
authorized merge, resulting-main verification and all charter boundaries remain required.

The [1 November standing continuation](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6556812743)
supersedes the earlier numerical review ceiling: necessary independent review and integration continue
with cumulative numbering and no reset. Full M0–M5 delivery remains authorized. Every substantive gate,
claim/WIP/readiness rule and resulting-main check remains required. The narrowly authorized one-file
owner-account workflow update is complete; ordinary writes and merges use the worker's own App.

The [1 November native-baseline exception](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6563215730)
authorizes resuming #3440's native baseline after its three failed runs (`1495b57a6`, `d82a0b7fa`,
`81965ed8a`), which stay recorded as Failed. Its scope is a stable locator for the reviewed size card
in the probe driver, a regression check for it and at most two fresh native attempts. It is not a waiver
of the three-failure stop or any other gate. The same record hands the Program to Claude. On the owner's
instruction, Claude [posts through the owner account with the `[Claude]` tag](https://github.com/elsa-workflows/elsa-foundation/issues/3440#issuecomment-6563355491)
while its App credentials are unavailable locally, so its PRs merge through Code Review, not standing self-merge.

[M1 is verified Done](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143),
merged through #3497 and qualified on resulting main `ab46bc22a`. Its package-only workflow and
separate-resource journeys establish the bounded minimum, with deterministic provider-protocol
rather than live-provider evidence. M2–M5 remain open. [Context admission #3439](https://github.com/elsa-workflows/elsa-foundation/issues/3439)
is verified Done through [PR #3514](https://github.com/elsa-workflows/elsa-foundation/pull/3514),
merged by the review App and qualified on resulting main `c0b47ceb1`. Its actual main CI,
Studio and separate-package witnesses are recorded on the owning issue; existing skips and
quarantined failures retain their original status. [Real effects #3440](https://github.com/elsa-workflows/elsa-foundation/issues/3440)
is the sole current implementation leaf, under [spec1186](../../specs/1186-weaver-effects/spec.md).
Its module-owned effects, acknowledged session autonomy and lifecycle follow-through retain
all five acceptance criteria. Review, required checks and resulting-main evidence remain
delivery gates for each subsequent leaf. The current
inventory also includes workflow library folders, tags and saved views added on main during M2.
#3440 is in review in PR #3612. Its acknowledged session autonomy, exact reviewed effects,
admission/ownership barriers, correlated expiry and owner-scoped audit are implemented. Spec 1186 is
Implemented and its session-policy contract is frozen as preview API. The normal composed Studio
gate (the complete browser suite) and the complete independent package witness first passed on
`7fc0a3c2e` and again on the final reviewed source `904671134`; #3440 records every run. The
witness covered the original 38 cases, the 4 current-authority cases, and all 13 lifecycle cases on
each actual adapter. This is deterministic provider-protocol evidence, not live-provider evidence.
Independent review, exact-head required checks, merge and resulting-main verification remain
before Done.

## Delivery plan

| Milestone epic | Outcome | Initial delivery leaves |
|---|---|---|
| [M0 #3429](https://github.com/elsa-workflows/elsa-foundation/issues/3429) | Baseline and activation | [Records/control surface #3435](https://github.com/elsa-workflows/elsa-foundation/issues/3435), [availability and context inventory #3436](https://github.com/elsa-workflows/elsa-foundation/issues/3436) |
| [M1 #3430](https://github.com/elsa-workflows/elsa-foundation/issues/3430) | Contribution contract proved before freeze | [Candidate contract #3437](https://github.com/elsa-workflows/elsa-foundation/issues/3437), [independent package witness #3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438) |
| [M2 #3431](https://github.com/elsa-workflows/elsa-foundation/issues/3431) | Shared context/tool/review/effect lifecycle | [Context and admission #3439](https://github.com/elsa-workflows/elsa-foundation/issues/3439), [real effects #3440](https://github.com/elsa-workflows/elsa-foundation/issues/3440), [supported recovery #3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441) |
| [M3 #3432](https://github.com/elsa-workflows/elsa-foundation/issues/3432) | Workflow authoring and inspection journeys | [Workflow delivery #3442](https://github.com/elsa-workflows/elsa-foundation/issues/3442) uses actual host catalogs, revisions, permissions and domain operations |
| [M4 #3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433) | Current-module coverage | [Module delivery #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) gives each inventory row an owning-module supported outcome or explicit limitation |
| [M5 #3434](https://github.com/elsa-workflows/elsa-foundation/issues/3434) | Integrated extensibility qualification | [Qualification #3444](https://github.com/elsa-workflows/elsa-foundation/issues/3444) covers providers, packaged/custom hosts, conformance and module-author guidance |

M0 precedes M1. M1 proves a workflow journey and a separately packaged non-workflow journey before
production contract freeze. After the required M2 mechanisms, M3 and M4 may proceed in separate
module lanes. M5 closes the complete matrix. Native blocking relationships record the concrete order;
feature issues own acceptance criteria and task checklists. Elaborate later leaves when their inputs
are concrete, using the normal feature/architecture workflow and any necessary contract decisions.

## Boundaries and acceptance

The defining extension test adds a resource, context, tool, renderer and observed applied result from
a separate package with zero shared-core, host or provider-adapter edits. Module load/removal changes
its contributions and supported availability. Consolidate the overlapping registries through the
existing composition model; do not introduce a universal domain schema or a second execution engine.

Backend operations use authorized owning-domain services. Unsaved browser-state operations use
module-owned adapters with correlated completion, exact target/revision checks and explicit behavior
when the originating context disappears. Distinguish proposed, approved, applied locally and persisted
outcomes. A missing executor fails explicitly; status alone never proves an effect. Preserve the
[workflow working-state apply](../adr/0002-direct-weaver-workflow-edits-target-designer-working-state.md)
and [Elsa-owned risk](../adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) decisions:
atomic visible edits, normal validation, dirty state, whole-batch undo and explicit save/publish.

Context is bounded, authorized and attributed to its resource/revision. Preserve caller attachments,
selection and unsaved-state fidelity; navigation cannot silently retarget a pending operation.
Distinguish trusted installed-module guidance from untrusted resource content. Module support grants
no blanket administration authority and exposes no secret values or unrelated data. Useful read-only
support is valid where a domain does not support mutation, with that limit explicit in the matrix.

Approval, denial, effective server autonomy, cancellation, disconnect/reconnect, retry, duplicate
delivery, restart and module removal need declared supported outcomes. Persist accepted decisions,
pending approvals and effect receipts where recovery requires them, without SDK objects, credentials
or hidden reasoning. Do not reconstruct vanished unsaved state or silently repeat accepted effects.
External effects retain their declared idempotency limits.

## Sequencing and WIP

Run alongside active programs, displace none and reactivate no parked backlog. Coordinate shared
Studio paths with [Expressions #2310](studio-expression-developer-experience.md),
[Compensation #3035](native-compensation.md) and [Unified Execution #2892](unified-execution.md).
Consume delivered [Hosting #3340](studio-hosting-package-reuse.md) and
[Unified Workspace #3392](unified-workflow-workspace.md) seams. Runtime agent composition and its
recovery semantics remain owned by #2892; this Program owns the Studio-wide assistant integration.
Releases, deployments, provider purchases and publisher activation are outside scope.

Kanban columns: Backlog, Ready, In progress, In review, Blocked and Done. The Kanban WIP view hides
Program/epic rollups; Roadmap retains the full hierarchy. One implementation leaf at a time and one
integration/review lane is the working policy, not an automated WIP enforcement claim. New delivery
issues enter needs-triage/Backlog; Ready requires maintainer triage, satisfied dependencies and the
repository readiness cap. Record transitions on the owning issue/PR and keep Project Status aligned.
The owner delegates this Program's triage, refinement and promotion when its inputs are sufficiently
clear; respect dependencies and existing claims. Direct owner-requested work may be claimed without
adding to the Ready queue. Independent subagents may assist investigation and review while the single
implementation leaf and integration lane remain under one owner.
Done requires verified completion; the setup task becoming Done does not complete M0 or the Program.

## Completion

Normal Studio and the supported package/custom-host journey must satisfy the finite current-context
matrix and the independent module extension test. Qualify both Copilot's delegated loop and
Anthropic's host-driven loop against the same tool/effect outcomes. Label deterministic,
provider-protocol and opt-in live evidence distinctly; source probes and historical MVP closure are
not current end-to-end acceptance. Meaningful risk-based regressions, required exact-head CI/review,
resulting-main verification, updated existing catalogs and module-author guidance support closure.
Keep unsupported/deferred boundaries explicit on #3428 until they are resolved or owner-amended.
