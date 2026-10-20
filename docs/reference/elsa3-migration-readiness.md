# Elsa 3 migration readiness: reusable workflows

What to check and change in Elsa 3 workflows **before** exporting them for the Elsa 4 collection import, so that the
import applies and every imported reusable activity publishes and runs. It covers workflows marked **Usable as
activity** (reusable workflows) and the workflows that call them. General conversion rules for every Elsa 3 activity are
in the [Elsa 3 mapping README](../../Extensions/Elsa3/src/Mapping/README.md#behaviour-worth-knowing).

This page describes the import as of specs 246, 247 and 248 (2026-10-20). JavaScript and Liquid rules for every workflow
are in the README's [Migrating Elsa 3 JavaScript](../../Extensions/Elsa3/src/Mapping/README.md#migrating-elsa-3-javascript);
this page adds what differs inside a reusable workflow. Each check names the diagnostic code the import reports if it is
not met. Every code comes with a message and guidance
in the API response; this page is the checklist, the response is the authority for one case.

## Status at a glance

Some limits are permanent differences between Elsa 3 and Elsa 4. Others are gaps in Elsa 4 graph activities that are
tracked and will be lifted; until then, the import refuses the workflow rather than import something that behaves
differently.

| Limit for a reusable workflow | Code | Kind |
|---|---|---|
| No start trigger in the body | `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER`, `ELS3-REUSABLE-UNSUPPORTED-TRIGGER` | Elsa 4 behaviour |
| No SetVariable, SetOutput, Correlate or SetName | `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` | Gap, [#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023) |
| No reads of its own inputs, its variables or other activities' results | `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED` | Gap, [#3027](https://github.com/elsa-workflows/elsa-foundation/issues/3027) |
| Outcomes: each Complete names one literal outcome, not `done` | `ELS3-ACTIVITY-UNSUPPORTED-MODE` | Elsa 4 behaviour |
| End only in the workflow's root flowchart | `ELS3-ACTIVITY-UNSUPPORTED-MODE` | Elsa 4 behaviour |

What this means today: a reusable workflow imports as a reusable activity when its body works from literal values and
signals its result through **outcomes**. It cannot return output values or compute from its inputs until #3023 and #3027
are done.

## Checklist per reusable workflow

1. **No trigger.** The workflow does not start from a trigger (such as HTTP Endpoint, Timer, Cron or Event) with
   *Can start workflow* ticked, nested or not. Move the trigger, with the activities it owns, into its own workflow
   that calls the reusable workflow.
   - Allowed: *Can start workflow* ticked on an activity that is not a trigger (a WriteLine, an If, a Flowchart, …). It only
     marks where the flowchart starts, and the import keeps that.
   - A custom activity type the import does not know counts as a trigger. If it is not one, clear *Can start workflow*
     on it and check that the flowchart still starts where it should: Elsa 3 then starts at the first activity nothing
     leads into.
2. **No setting values.** The body uses no SetVariable, SetOutput, Correlate or SetName activity. No JavaScript or
   Liquid assigns a value either (`setVariable`, `setOutput`, `setInput`, `setResult`, a generated `set{Name}(...)`,
   `setCorrelationId`, `setWorkflowInstanceName`, `variables.x = ...`, Liquid `assign`): that is refused in every
   workflow (`VF-IMP-005`), and the Set, Correlate or Set Name activity its guidance points to is not available in a
   reusable body yet.
3. **No reading values.** No activity in the body reads a value: in JavaScript `getInput(...)`, `getVariable(...)`,
   `variables.x` or a generated `get{Name}()` accessor; in Liquid `Input.x`, `Variables.x` or `Output.x`; and no property
   bound to a variable or to another activity's output (an Elsa 3 memory reference). Use literal values.
   - Allowed: `getWorkflowInstanceId()` and `getCorrelationId()` read the running workflow instance, as in Elsa 3.
   - The JavaScript guidance "pass the value in as a workflow input" or "capture the output into a variable" does not
     help inside a reusable body, because the body cannot read either yet. Keep that logic in the calling workflow, or
     make the reusable workflow a regular one (see below). Declared inputs may stay on the workflow, and callers may pass them,
   but the body cannot read them yet.
4. **Outcomes.** Each Complete names exactly one literal outcome; not a list, not an expression, and not `done`, which
   collides with Elsa 4's natural outcome. Declare the outcomes on the workflow: an undeclared one is declared anyway with
   `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED`, and a declared one no Complete reaches is dropped with
   `ELS3-BEHAVIOUR-OUTCOME-UNREACHED`. End is allowed only in the workflow's root flowchart.
5. **Version numbers.** If the workflow changed, it has a new version number in Elsa 3. An Elsa 4 version that is
   already imported cannot be replaced; a changed workflow under the same number is refused at publish with
   `ELS3-REUSABLE-IDENTITY-COLLISION`.

## When a reusable workflow cannot meet the checklist yet

Clear **Usable as activity** on it in Elsa 3 and re-export. It then imports as a regular workflow, where triggers,
variables, inputs, outputs and Set activities convert as usual. Every workflow that used it as an activity must call it
with **Dispatch Workflow** or **Execute Workflow** instead; otherwise that caller is refused with
`ELS3-REUSABLE-UNSUPPORTED-REFERENCE`.

- The import keeps the Elsa 3 definition id as the Elsa 4 workflow definition id, so the dispatching activity's
  definition id stays as it is.
- Publish the called workflow before the caller.
- Execute Workflow becomes a durable dispatch in Elsa 4 rather than an in-process call
  (`ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE`); review whether the caller depends on in-process behaviour.

## Checklist per collection

1. **Complete selection.** Select every reusable version the selected workflows use (`ELS3-REUSABLE-SELECTION-NOT-CLOSED`).
2. **No recursion.** No reusable workflow uses itself, directly or through others (`ELS3-REUSABLE-DEPENDENCY-CYCLE`).
3. **No crosswise versions.** Elsa 4 publishes each workflow's versions oldest first. If an older version of one workflow
   uses a newer version of another while an older version of that one uses a newer version of the first, no order works
   (`ELS3-REUSABLE-DEPENDENCY-CYCLE`). Select fewer versions, for example only the newest of each.
4. **Older versions first.** An older version can be imported only while no newer version of the same workflow is
   published. Import the versions you need together in one apply; an older one added later is refused with
   `activity.version.bump-insufficient`.

## Before the first import (operators)

- The host composes `Elsa3ImportActivitiesPublishing` (it needs `WorkflowsPublishingApi`); without it, an apply that
  selects a reusable workflow is refused with `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`.
- Built-in activities come from the host's CLR reconciliation. An activity the host's catalog lacks is refused at
  conversion (`ELS3-ACTIVITY-VERSION-UNAVAILABLE`); one in the catalog but not published is refused at publish with the
  publisher's dependency diagnostic.
- The engineer who applies an import holds `elsa3-import:manage` and `workflows/publishing:manage`, because the apply
  publishes the imported activities.

## After an apply

The commit is atomic; publishing is per version. Read the receipt's `publications`, not the sources' dispositions
(`Created` there only means "not published when the commit ran"):

| `status` | Meaning | What to do |
|---|---|---|
| `Published` / `AlreadyPublished` | The version runs as a graph activity. | Nothing. |
| `Refused` | The publisher refused it; `errorCode` and `diagnostics` say why. | Follow the diagnostic's remediation. |
| `NotPublishedDependency` | A version it uses was not published (`blockedBy`). | Fix that version first. |

To finish a partly published import, upload the same export again and apply it under a **new idempotency key**: the
import reuses what it created and publishes only what is missing. A replay of the same key or a status read reports the
current state without publishing.

Review the `warnings` (`ELS3-BEHAVIOUR-*`): they describe where Elsa 4 behaves differently at run time, and they stay on
the receipt.
