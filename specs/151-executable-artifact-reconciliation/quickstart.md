# Quickstart: Executable Artifact Reconciliation (spec 151)

Three walks, matching US3 → US1 → US4. Feature ids and options per contracts/runtime-contracts.md.

## 1. Export from a design/build engine (US3)

On a publish-capable engine (`WorkflowsPublishingApi` composed):

```bash
# publish as usual (writes the artifact to the executable store — existing behavior)
# then export the closure — the v1 "download" target returns the bytes:
curl -H "Authorization: ..." \
  "https://design-engine/{shell}/publishing/workflows/{versionId}/executable-export" \
  -o my-workflow-closure.json
```

The endpoint is discovered via `GET /capabilities` → capability `elsa.api.publishing`, rel `workflow-executable-export` (what Studio's "Export executable artifact" action uses). The file contains the artifact + its transitive child-workflow closure + published source references + trigger bindings (see contracts/closure-envelope.md). Only Published-scope versions export; test-run versions are refused.

## 2. Run a design-free runtime that imports the closure (US1/US2)

Compose a runtime-only engine — no design, no publishing, no compiler — with the artifact reconciliation feature. CShells appsettings (shape mirrors existing feature composition):

```jsonc
{
  "Features": {
    "Tasks": {},
    "ActivitiesRuntime": {},            // registers activity types (the reconciler orders itself after this)
    "WorkflowsRuntimeApi": {},          // or any composition that arms AddWorkflowRuntime()
    "WorkflowsRuntimeTriggers": {},     // stimulus routing for trigger-started workflows
    "JsonWorkflowArtifactReconciliation": {
      "FolderPath": "/mnt/artifacts",   // or FilePath / ordered Files
      "SourceId": "prod-artifact-drop",
      // "TenantId": "tenant-a"         // optional; default null
    }
    // + a Groundwork runtime persistence feature for durability (in-memory otherwise)
  }
}
```

Drop `my-workflow-closure.json` into `/mnt/artifacts` and start the shell. At activation, the reconciler (single-node, distributed-locked, before readiness):

1. parses each closure file (malformed/unknown formatVersion → loud rejection, no partial import);
2. validates the dependency closure against the envelope alone (missing child / hash mismatch / cycle → parent rejected) and recomputes each artifact's content hash (corrupted payload → rejected before persistence);
3. runs the requirements preflight — consumer capabilities, storage drivers, **and** CLR activity-type presence — rejecting unsatisfiable artifacts **at import** with a diagnostic naming what's missing (never an `UnknownActivityTypeException` at first activation);
4. activates survivors through the **shared activation coordinator** (the same one publishing uses): source reference minted, trigger bindings **and recurring schedules** recomputed and activated, trigger-index observers notified (routes refresh), and the definition's activation slot flipped under CAS (latest-wins, exactly one active version per definition).

All gates run for a complete closure unit (root + dependencies) before anything is written — a failed unit writes nothing. Verify: start the workflow by artifact id via the runtime API, or fire its HTTP/timer stimulus — routing and timers work because the full projection set was activated from the artifact. A mixed batch activates the satisfiable closure units and rejects the rest individually.

## 3. Roll out v2 (US4)

Copy the v2 closure into the folder and reload the shell via the existing shell-reload API (re-runs startup tasks — no new trigger machinery):

- v2 becomes active; v1's publication is deactivated and its minted reference retired (`publication-replaced`). In-flight v1 instances finish on v1.
- Re-running reconcile over an unchanged folder is a no-op (content-addressed create-only store + slot revision CAS) — exactly one active version per definition, no duplicates.
- On a combined engine (US5), the activation slot's explicit ownership decides conflicts: the same artifact arriving via both publish and import is an idempotent no-op; a *different* artifact from the non-owning source is rejected loudly with a diagnostic naming the owning activation source — never a silent double activation.

## Verifying the composition claim (SC-B-001/005)

The runtime-only composition test asserts no `Elsa.Workflows.Design.*` / `Elsa.Workflows.Publishing*` / `Elsa.Activities.Design.*` assembly is loaded while executing an imported artifact end-to-end — the assembly-enforced boundary this feature exists to serve.
