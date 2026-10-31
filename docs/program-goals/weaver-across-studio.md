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

[M1 is verified Done](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6539679143),
merged through #3497 and qualified on resulting main `ab46bc22a`. Its package-only workflow and
separate-resource journeys establish the bounded minimum, with deterministic provider-protocol
rather than live-provider evidence. M2–M5 remain open. [Context admission #3439](https://github.com/elsa-workflows/elsa-foundation/issues/3439)
is the sole current implementation leaf; its [spec1176](../../specs/1176-weaver-turn-context/spec.md)
retains all six acceptance areas and the module ownership boundary.

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
